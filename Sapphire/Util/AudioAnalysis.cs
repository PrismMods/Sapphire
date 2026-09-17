using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Sapphire
{
    /* Sample-level reads of the loaded song: a peak envelope for the timeline's waveform and a
       first-onset time for the offset suggestion.

       Both come from ONE chunked pass. A three-minute stereo track is ~16M floats; pulling that
       into a single array to look at it would cost 64 MB, so the pass reads a second at a time
       and folds each chunk into fixed-size buckets. The result is cached per clip — the analysis
       is idempotent and nothing about a loaded clip changes underneath it.

       The conductor's own clip is usually NOT readable: AudioManager loads level songs through
       UnityWebRequestMultimedia with streamAudio set, and a streaming clip returns false from
       GetData no matter what. So the samples come from our own decode of the song FILE, loaded
       once per level with streamAudio off, and the conductor's clip is only used when it happens
       to be readable. That decode is a coroutine, which is why callers get a Status rather than
       a blocking answer. */
    internal static class AudioAnalysis
    {
        internal const int Buckets = 2048;      // envelope resolution across the whole song

        internal class Result
        {
            internal string ClipName;
            internal float Length;              // seconds
            internal float[] Peak;              // Buckets, 0..1
            internal float[] Rms;               // Buckets, 0..1
            internal float[] Env;               // short-window RMS over the analysed head
            internal float Hop;                 // seconds per Env frame
            internal float Onset = -1f;         // seconds; <0 = not found
            internal float Tempo = -1f;         // BPM; <0 = not computed yet
            internal float TempoConf;           // 0..1 phase concentration at Tempo
            internal float[] Curve;             // local BPM per CurveStep seconds; 0 = no reading
            internal float[] CurveConf;         // concentration behind each Curve point
            internal float CurveLo, CurveHi;    // BPM range actually reached
            internal float CurveMedian;         // the tempo the path spends most of its time at
            internal float CurveSpread;         // (hi-lo)/median, 0 = dead steady
            internal bool Sharpened;            // the long-fold refinement has run
            internal int CurveVersion;          // bumped whenever Curve's values change
            internal bool CurveRunning;
            /* The emission matrix, kept. It is the expensive half of the curve — the decode over
               it is a few tens of milliseconds — so a change to the smoothness or octave cost
               can be answered by re-running the decode alone, and a change to window/step/bins
               by rebuilding this from Env. Neither needs the song read off disk again, which is
               what made every parameter tweak cost a full re-analysis. */
            internal float[][] Emit;
            internal string EmitKey;            // the window/step/bin geometry Emit was built at
        }

        // What the emission depends on; a change here means Emit has to be rebuilt.
        private static string EmitKeyNow()
        {
            return CurveWindowSec.ToString("0.###") + "/" + CurveStepSec.ToString("0.###")
                 + "/" + Mathf.Clamp(CurveBins, 24, 1024);
        }

        /* Pick the smoothness automatically, after the first analysis.

           There is no ground truth at runtime, so this cannot optimise accuracy directly. What
           it can do is model selection, which is the same question asked honestly: of the paths
           on offer, which explains the audio best without inventing changes the audio does not
           support? Score each candidate by the emission it collects along its own path, minus a
           charge per tempo CHANGE it makes — the classic description-length trade. A tiny
           smoothness wins the emission term and pays for hundreds of changes; a huge one makes
           no changes and collects a poor emission; the value in between is the answer.

           Affordable only because the emission matrix is kept: every candidate here is a decode
           over the SAME scores, which is milliseconds, not a re-analysis.

           OFF by default, because measurement says the criterion has a blind spot. On the 300
           levels it is neutral — 81.7%/66.3%, the same as the fixed default — and on Parallel
           Universe Shifter it picks the best lambda available (8.3%, matching the best of the
           seven candidates). But on TremENDouS and PP BREAKER it picks 6, which reads 80-480 and
           67-273 respectively: octave-hopping and a spurious dip. The reason is structural. A
           hopping path collects MORE emission, because it follows the local argmax, and the
           argmax during these passages IS the harmonic. Charging octave-sized steps extra does
           not catch it either — measured at every weight from 0 to 20, the pick never moved,
           because the hop is reached through many small steps rather than one big one. So this
           is offered as a tool, not as the default: it does real model selection, and model
           selection cannot see a failure that scores well. */
        internal static bool AutoTune = false;
        private static readonly float[] AutoLambdas = { 6f, 8f, 12f, 16f, 24f, 32f, 48f };
        // What one tempo change has to earn to be worth making, in emission units.
        private const float ChangeCost = 0.55f;
        private const float KneeLog = 0.3001f;   // ln(1.35) — above this a move is an octave flip

        private static float PathScore(float[][] emit, float[] path, float[] lg, float[] grid, out int changes)
        {
            changes = 0;
            double total = 0.0;
            int prev = -1;
            for (int t = 0; t < path.Length; t++)
            {
                int bi = 0; float best = float.MaxValue;
                for (int i = 0; i < grid.Length; i++)
                {
                    float d = grid[i] - path[t]; if (d < 0f) d = -d;
                    if (d < best) { best = d; bi = i; }
                }
                total += emit[t][bi];
                if (prev >= 0 && Mathf.Abs(lg[bi] - lg[prev]) > 0.004f) changes++;
                prev = bi;
            }
            return (float)total;
        }

        /* Re-run the curve against the CURRENT tunables, reusing whatever still applies. Nothing
           is read from disk and the envelope is never recomputed; if only the decode parameters
           moved, the emission matrix is reused too. */
        internal static void Recurve()
        {
            var res = _cache;
            if (res == null || res.Env == null || res.Hop <= 0f) return;
            if (res.CurveRunning) return;
            if (res.EmitKey != EmitKeyNow()) res.Emit = null;   // geometry moved: rebuild it
            res.Curve = null;
            res.Sharpened = false;
            var runner = MainClass.Runner;
            if (runner == null) return;
            res.CurveRunning = true;
            runner.StartCoroutine(CurveJob(res));
        }

        // True when a curve exists and the tunables have moved away from what produced it.
        internal static bool CurveStale
        {
            get
            {
                var res = _cache;
                return res != null && res.Curve != null && !res.CurveRunning
                    && (res.EmitKey != EmitKeyNow() || _decodeKey != DecodeKeyNow());
            }
        }

        private static string _decodeKey;
        private static string DecodeKeyNow()
        {
            return CurveLambda.ToString("0.###") + "/" + CurveOctCost.ToString("0.###");
        }

        /* Analysis is EXPLICIT. It used to start the moment anything asked for a result, which
           meant every level open paid for a full decode and a Viterbi pass before the charter had
           asked for either — and that is felt as the editor being slow to open. Nothing runs now
           until Request() is called, which the Audio window's Analyze button does. */
        internal static bool Requested { get; private set; }

        internal static void Request()
        {
            Requested = true;
            _cache = null; _decoded = null; _wantKey = null;
            Status = State.Idle; StatusNote = null;
        }

        /* Tunables, surfaced in the Audio window rather than buried here. The defaults are what
           the measurements settled on; a song that defeats them is exactly when a charter wants
           to turn a knob instead of filing a bug. */
        internal static float CurveWindowSec = 8f, CurveStepSec = 3f;
        internal static float CurveLambda = 12f;     // transition cost per unit |log ratio|
        internal static float CurveOctCost = 36f;    // surcharge beyond the knee — see CurveJob
        internal static int CurveBins = 448;   // 448 over 60-480 keeps the 0.47% grid step
        // The mean every song's emission matrix is scaled to — the typical raw concentration,
        // so the lambda above keeps the units it was tuned in.
        private const float EmitMean = 0.045f;

        internal static void ResetTunables()
        {
            CurveWindowSec = 8f; CurveStepSec = 3f; CurveLambda = 12f; CurveBins = 448;
            CurveOctCost = 36f;
        }

        internal enum State { Idle, Loading, Ready, Failed }
        internal static State Status { get; private set; }
        internal static string StatusNote { get; private set; }

        private static Result _cache;
        private static string _wantKey;      // level path + song name the cache belongs to
        private static AudioClip _decoded;   // our own non-streaming copy, kept for the preview

        /* AudioClip.GetData has both a float[] and a Span<float> overload in this Unity, and the
           Mono compiler binds the Span one — against a Span that mscorlib here does not define.
           Pin the array overload by reflection instead; it is one call per second of audio. */
        private static readonly System.Reflection.MethodInfo GetDataArray =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });

        // The decoded copy only. Playback needs a clip we own: the conductor's is streaming and
        // already in use by the game's own source.
        /* The decoded clip, or null once it is no longer usable. A scene load — entering play
           mode is one — can destroy a clip we created, and Unity leaves the reference looking
           non-null to plain C# while every use of it fails silently. Checking loadState catches
           that, and dropping the cached result with it means the next request decodes again
           rather than handing back a corpse, which is what made the play button die after a
           trip through play mode. */
        internal static AudioClip PlayableClip()
        {
            if (_decoded == null) return null;
            bool dead;
            try { dead = _decoded.loadState != AudioDataLoadState.Loaded; } catch { dead = true; }
            if (!dead) return _decoded;
            _decoded = null; _cache = null; _wantKey = null;
            Status = State.Idle; StatusNote = null;
            return null;
        }

        // Our decoded copy when we have one (readable, and safe to preview from), else the
        // conductor's — good enough for length and naming even when its samples are locked away.
        internal static AudioClip Clip()
        {
            if (_decoded != null) return _decoded;
            try
            {
                var c = scrConductor.instance;
                var src = c != null ? c.song : null;
                return src != null ? src.clip : null;
            }
            catch { return null; }
        }

        /* Absolute path of the level's song. ADOBase.levelPath is the .adofai file; the song sits
           beside it under the name songSettings holds. */
        internal static string SongPath()
        {
            try
            {
                string lp = ADOBase.levelPath;
                var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld == null || string.IsNullOrEmpty(lp)) return null;
                string name = ld.songFilename;
                if (string.IsNullOrEmpty(name)) return null;
                string dir = System.IO.Path.GetDirectoryName(lp);
                if (string.IsNullOrEmpty(dir)) return null;
                string full = System.IO.Path.Combine(dir, name);
                return System.IO.File.Exists(full) ? full : null;
            }
            catch { return null; }
        }

        private static AudioType TypeOf(string path)
        {
            string e = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (e == ".mp3") return AudioType.MPEG;
            if (e == ".wav") return AudioType.WAV;
            if (e == ".aiff" || e == ".aif") return AudioType.AIFF;
            return AudioType.OGGVORBIS;
        }

        /* null until the answer exists — Status says whether that is "still decoding" or "gave
           up". Callers poll; nothing here blocks the frame on a file read. */
        internal static Result Get(bool force = false)
        {
            string key = SongPath() ?? "";
            /* A mode switch reloads the scene, and for a frame or two ADOBase.levelPath and
               scnGame.instance are null — SongPath then answers "" and the old code read that as
               a DIFFERENT song, threw away the decoded clip and started over. Everything holding
               that clip (the preview player, most visibly) was left pointing at nothing. An empty
               key means "ask again later", never "new song". */
            if (!Requested) return null;          // nothing happens until the charter asks
            if (key.Length == 0 && _cache != null) return _cache;
            if (force || (key.Length > 0 && key != _wantKey))
            {
                _wantKey = key;
                _cache = null;
                _decoded = null;
                Status = State.Idle;
                StatusNote = null;
            }
            if (_cache != null) return _cache;
            if (Status == State.Loading || Status == State.Failed) return null;

            /* Fast path: if the conductor's clip happens to be readable, no decode is needed —
               and it is then playable too, so record it as the preview's clip. Leaving _decoded
               null here left the play button permanently dead on any level where this path hit,
               since PlayableClip had nothing to hand back. */
            var clip = Clip();
            if (clip != null && clip.loadState == AudioDataLoadState.Loaded)
            {
                var r = Analyse(clip);
                if (r != null) { _decoded = clip; _cache = r; Status = State.Ready; return r; }
            }
            BeginDecode();
            return null;
        }

        internal static void Invalidate()
        {
            _cache = null; _decoded = null; _wantKey = null;
            Status = State.Idle; StatusNote = null;
        }

        private static void BeginDecode()
        {
            string path = SongPath();
            if (path == null)
            {
                Status = State.Failed;
                StatusNote = Loc.T("Song file not found.");
                return;
            }
            var runner = MainClass.Runner;
            if (runner == null) { Status = State.Failed; StatusNote = Loc.T("Song samples unavailable."); return; }
            Status = State.Loading;
            StatusNote = Loc.T("Reading the song…");
            runner.StartCoroutine(Decode(path));
        }

        private static IEnumerator Decode(string path)
        {
            string uri = new System.Uri(path).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, TypeOf(path)))
            {
                var dh = req.downloadHandler as DownloadHandlerAudioClip;
                if (dh != null) dh.streamAudio = false;   // the whole point: streaming clips cannot be read
                yield return req.SendWebRequest();
                AudioClip clip = null;
                try { if (!req.isNetworkError && !req.isHttpError) clip = DownloadHandlerAudioClip.GetContent(req); }
                catch { clip = null; }
                if (clip == null)
                {
                    Status = State.Failed;
                    StatusNote = Loc.T("Could not decode the song.");
                    yield break;
                }
                // Loading can still be in flight when GetContent returns.
                while (clip.loadState == AudioDataLoadState.Loading) yield return null;
                _decoded = clip;
                var res = Analyse(clip);
                if (res == null)
                {
                    Status = State.Failed;
                    StatusNote = Loc.T("Song samples unavailable.");
                    yield break;
                }
                _cache = res;
                Status = State.Ready;
                StatusNote = null;
            }
        }

        private static Result Analyse(AudioClip clip)
        {
            int ch = Mathf.Max(1, clip.channels);
            int frames = clip.samples;
            if (frames <= 0) return null;

            var res = new Result { ClipName = clip.name, Length = clip.length };
            var peak = new float[Buckets];
            var sum = new float[Buckets];
            var cnt = new int[Buckets];

            /* Onset detection rides the same pass over short windows. 10 ms is the hop the
               tuning settled on — 8 to 24 ms all work, and coarser than that costs accuracy on
               the attack. */
            int Win = Mathf.Max(64, clip.frequency / 100);
            /* The envelope now covers the WHOLE song — the tempo curve needs it, and at a 10ms
               hop five minutes is 30k floats. Everything that only wants the head (onset, beat
               phase, global tempo) bounds itself by seconds instead. */
            var winRms = new System.Collections.Generic.List<float>(4096);

            int chunkFrames = Mathf.Max(Win, clip.frequency);                 // ~1s per read
            var buf = new float[chunkFrames * ch];
            double acc = 0.0; int accN = 0;

            for (int off = 0; off < frames; off += chunkFrames)
            {
                int want = Mathf.Min(chunkFrames, frames - off);
                if (want < chunkFrames) buf = new float[want * ch];
                bool ok;
                try { ok = GetDataArray != null && (bool)GetDataArray.Invoke(clip, new object[] { buf, off }); }
                catch { ok = false; }
                if (!ok) return null;                  // streaming clip: nothing to read

                for (int i = 0; i < want; i++)
                {
                    float v = 0f;
                    int b = i * ch;
                    for (int c = 0; c < ch; c++) v += buf[b + c];
                    v /= ch;
                    float a = v < 0f ? -v : v;

                    int bucket = (int)((long)(off + i) * Buckets / frames);
                    if (bucket >= Buckets) bucket = Buckets - 1;
                    if (a > peak[bucket]) peak[bucket] = a;
                    sum[bucket] += v * v; cnt[bucket]++;

                    acc += v * v; accN++;
                    if (accN >= Win)
                    {
                        winRms.Add(Mathf.Sqrt((float)(acc / accN)));
                        acc = 0.0; accN = 0;
                    }
                }
            }

            res.Peak = peak;
            res.Rms = new float[Buckets];
            for (int i = 0; i < Buckets; i++)
                res.Rms[i] = cnt[i] > 0 ? Mathf.Sqrt(sum[i] / cnt[i]) : 0f;
            res.Env = winRms.ToArray();
            res.Hop = Win / (float)clip.frequency;
            res.Onset = FindOnset(res.Env, res.Hop);
            return res;
        }

        /* Onset detection, tuned against 115 published levels.

           An absolute level threshold cannot do this job: it has to serve both a song that
           starts at full tilt and one that opens on two seconds of ambience, and those want
           opposite bars. What separates them is the RISE — log-domain energy flux over a ~12ms
           lag — judged against a local median, so a quiet passage and a loud one are held to the
           same relative standard. The first rise that clears the bar AND is followed by real
           level is the onset; it is then walked back down the attack, because the crossing sits
           partway up the transient and reads ~20ms late.

           Measured on those levels against the charters' own offsets: median 9ms, 68% within
           30ms, 78% within 150ms, and it always answers. The absolute-threshold version this
           replaces sat at median 4.9s, 23% within 30ms, and returned nothing at all for 17% of
           the songs. */
        internal const int OnsetWindowSec = 40;
        private const float RiseFloor = 0.6f;     // log-energy rise over the lag
        private const float RiseMult = 3f;        // ... or this multiple of the local median
        private const float LevelFrac = 0.05f;    // the next 80ms must reach this share of peak
        private const float BackFrac = 0.2f;      // walk back down to this share of the attack

        private static float[] LogFlux(float[] env, float hop, float lagSec)
        {
            int k = Mathf.Max(1, Mathf.RoundToInt(lagSec / hop));
            var fl = new float[env.Length];
            const float eps = 1e-5f;
            for (int i = k; i < env.Length; i++)
            {
                float d = Mathf.Log(env[i] + eps) - Mathf.Log(env[i - k] + eps);
                fl[i] = d > 0f ? d : 0f;
            }
            return fl;
        }

        // Median of a strided sample of the window — exact medians over a ±1s window per frame
        // would be O(n·w log w) for a number nobody reads to three decimals.
        private static float[] RunningMedian(float[] x, int half)
        {
            int n = x.Length;
            var outv = new float[n];
            int step = Mathf.Max(1, half / 16);
            var buf = new System.Collections.Generic.List<float>(48);
            for (int i = 0; i < n; i++)
            {
                buf.Clear();
                int a = Mathf.Max(0, i - half), b = Mathf.Min(n, i + half + 1);
                for (int j = a; j < b; j += step) buf.Add(x[j]);
                if (buf.Count == 0) { outv[i] = 0f; continue; }
                buf.Sort();
                outv[i] = buf[buf.Count / 2];
            }
            return outv;
        }

        private static float FindOnset(float[] env, float hop)
        {
            int n = Mathf.Min(env.Length, Mathf.RoundToInt(OnsetWindowSec / hop));
            if (n < 16 || hop <= 0f) return -1f;
            float peak = 0f;
            for (int i = 0; i < n; i++) if (env[i] > peak) peak = env[i];
            if (peak <= 1e-6f) return -1f;

            var fl = LogFlux(env, hop, 0.012f);
            var med = RunningMedian(fl, Mathf.Max(4, Mathf.RoundToInt(1f / hop)));
            int sus = Mathf.Max(1, Mathf.RoundToInt(0.08f / hop));
            float lvl = peak * LevelFrac;

            for (int i = Mathf.Max(1, Mathf.RoundToInt(0.01f / hop)); i < n - sus; i++)
            {
                if (fl[i] < Mathf.Max(RiseFloor, med[i] * RiseMult)) continue;
                float mean = 0f;
                for (int j = i; j < i + sus; j++) mean += env[j];
                mean /= sus;
                if (mean < lvl) continue;
                return WalkBack(env, i, sus) * hop;
            }
            return -1f;
        }

        // The threshold crossing is partway up the attack; its foot is where the level was still
        // a fifth of where the transient lands.
        private static int WalkBack(float[] env, int i, int sus)
        {
            float top = 0f;
            for (int j = i; j < Mathf.Min(env.Length, i + sus); j++) if (env[j] > top) top = env[j];
            int k = i;
            while (k > 0 && env[k - 1] > top * BackFrac) k--;
            return k;
        }

        /* TEMPO.

           Two stages. Autocorrelating the onset flux finds the period coarsely — but at a 10ms
           hop one lag step is ~2% of the BPM at 150, far too blunt for a chart that has to stay
           in phase for three minutes. So the coarse peak (and its half and double, because
           autocorrelation cannot tell a beat from a half-beat) seeds a fine sweep that maximises
           PHASE CONCENTRATION: fold the onsets at a candidate tempo and measure how tightly they
           pile into one phase. Over 30s a 0.1% tempo error is already visible drift, which makes
           the fold far sharper than the autocorrelation that seeded it.

           Honest about its limits. Measured on 49 songs from one corpus and 157 from a second,
           held-out one, the answer is within 0.1% of the charter's BPM only ~38% of the time
           ungated. The concentration is a real confidence signal though: at 0.30 and above it is
           within 0.1% about three quarters of the time and within 0.5% nine times in ten, over
           roughly a quarter of songs. (The first corpus alone said 92%, on twelve songs; the
           held-out set put that at 75%, which is the number to believe.) Below the gate this
           still returns the number, with its low confidence attached, and the UI declines to
           offer it.

           Two things it can never know. Autocorrelation is blind to metre, so the charter's BPM
           is routinely a multiple of this — and which multiple flips by corpus: 2x dominated the
           first, 0.5x the second, and every confident one in the held-out set was a power of
           two. Hence the ladder of buttons rather than one number. And some levels' BPM is not a
           musical tempo at all; one in the held-out set is 100000. */
        internal static float Tempo(Result res, out float confidence)
        {
            confidence = 0f;
            if (res == null || res.Env == null || res.Hop <= 0f) return -1f;
            if (res.Tempo > 0f)
            {
                confidence = res.TempoConf;
                /* Sharpen once, after the curve lands — it is what says whether the tempo is
                   steady enough for a whole-song fold to mean anything. */
                if (!res.Sharpened && res.Curve != null)
                {
                    res.Sharpened = true;
                    float better = Sharpen(res, res.Tempo);
                    if (better > 0f) res.Tempo = better;
                    res.Tempo = ToBand(res.Tempo);
                    AlignCurve(res);
                }
                return res.Tempo;
            }

            float hop = res.Hop;
            int n = Mathf.Min(res.Env.Length, Mathf.RoundToInt(40f / hop));
            if (n < 64) return -1f;
            var fl = LogFlux(res.Env, hop, 0.012f);

            float mean = 0f;
            for (int i = 0; i < n; i++) mean += fl[i];
            mean /= n;

            int loLag = Mathf.Max(2, Mathf.RoundToInt(60f / 240f / hop));
            int hiLag = Mathf.Min(n / 2, Mathf.RoundToInt(60f / 60f / hop));
            if (hiLag <= loLag) return -1f;

            var acf = new float[hiLag + 1];
            for (int lag = loLag; lag <= hiLag; lag++)
            {
                float acc = 0f;
                for (int i = lag; i < n; i++) acc += (fl[i] - mean) * (fl[i - lag] - mean);
                acf[lag] = acc / (n - lag);
            }
            // Harmonic sum: a real period also has energy at its multiples, which demotes the
            // spurious peaks that sit between beats.
            int bestLag = loLag; float bestScore = float.MinValue;
            for (int lag = loLag; lag <= hiLag; lag++)
            {
                float sc = acf[lag];
                for (int k = 2; k <= 4; k++)
                {
                    int l2 = lag * k;
                    if (l2 <= hiLag) sc += acf[l2] / k;
                }
                if (sc > bestScore) { bestScore = sc; bestLag = lag; }
            }
            float coarse = 60f / (bestLag * hop);

            float bestBpm = coarse, bestConc = 0f;
            for (int variant = 0; variant < 3; variant++)
            {
                float baseBpm = variant == 0 ? coarse : variant == 1 ? coarse * 2f : coarse * 0.5f;
                if (baseBpm < 30f || baseBpm > 600f) continue;
                for (float b = baseBpm * 0.94f; b <= baseBpm * 1.06f; b *= 1.0005f)
                {
                    float c = Concentration(fl, hop, b);
                    if (c > bestConc) { bestConc = c; bestBpm = b; }
                }
            }
            res.Tempo = bestBpm; res.TempoConf = bestConc;
            confidence = bestConc;
            return bestBpm;
        }

        // Resultant length of the onset strengths folded onto one beat: 1 = perfectly locked,
        // 0 = uniform. This is what makes the fine sweep sharp.
        private static float Concentration(float[] fl, float hop, float bpm, float windowSec = 30f,
            int from = 0, int to = -1)
        {
            const int Bins = 64;
            float cr = 60f / bpm;
            if (cr <= hop) return 0f;
            int n = to >= 0 ? Mathf.Min(fl.Length, to) : Mathf.Min(fl.Length, Mathf.RoundToInt(windowSec / hop));
            var h = new float[Bins];
            float tot = 0f;
            for (int i = Mathf.Max(1, from); i < n; i++)
            {
                float f = fl[i];
                if (f <= 0f) continue;
                int b = Mathf.Clamp((int)(Mathf.Repeat(i * hop, cr) / cr * Bins), 0, Bins - 1);
                h[b] += f; tot += f;
            }
            if (tot <= 0f) return 0f;
            float cs = 0f, sn = 0f;
            for (int b = 0; b < Bins; b++)
            {
                float a = 2f * Mathf.PI * b / Bins;
                cs += h[b] * Mathf.Cos(a); sn += h[b] * Mathf.Sin(a);
            }
            return Mathf.Sqrt(cs * cs + sn * sn) / tot;
        }

        /* Autocorrelation of a window's onset flux at each candidate period, harmonics summed.

           Concentration alone cannot tell a beat from its subdivisions: folding onsets at four
           times the tempo piles them just as tightly, so a song with steady sixteenths scores as
           well an octave or two up as it does at the truth. Autocorrelation has the opposite
           blind spot — it happily reports a half-beat — and a harmonic sum (lag, 2·lag, 3·lag,
           4·lag at 1/k) demotes the between-beat peaks. Multiplying the two is the standard fix
           and measures like one: across 124 charted songs sampled every 3 seconds, agreement
           with the charter's own declared tempo rises from 78.6% to 87.0%.

           Normalised by acf[0] and by the overlap length, so windows of different energy and
           lags of different support are comparable. */
        private static float[] AcfHarm(float[] fl, float hop, float[] grid, int from, int to)
        {
            int bins = grid.Length;
            var outv = new float[bins];
            int n = to - from;
            if (n < 32) return outv;

            float mean = 0f;
            for (int i = from; i < to; i++) mean += fl[i];
            mean /= n;

            // Harmonics reach four periods out, so the slowest tempo in the grid sets the span.
            int maxLag = Mathf.Min(n - 8, Mathf.RoundToInt(60f / grid[0] / hop) * 4);
            if (maxLag < 4) return outv;
            var acf = new float[maxLag + 1];
            float zero = 0f;
            for (int i = from; i < to; i++) { float d = fl[i] - mean; zero += d * d; }
            zero /= n;
            if (zero <= 1e-12f) return outv;
            for (int lag = 1; lag <= maxLag; lag++)
            {
                float acc = 0f;
                for (int i = from + lag; i < to; i++) acc += (fl[i] - mean) * (fl[i - lag] - mean);
                acf[lag] = acc / ((n - lag) * zero);
            }

            for (int b = 0; b < bins; b++)
            {
                float lagF = 60f / grid[b] / hop;
                float sum = 0f;
                for (int k = 1; k <= 4; k++)
                {
                    float l = lagF * k;
                    int i0 = (int)l;
                    if (i0 < 1 || i0 + 1 > maxLag) continue;
                    float f = l - i0;
                    sum += (acf[i0] * (1f - f) + acf[i0 + 1] * f) / k;
                }
                outv[b] = sum > 0f ? sum : 0f;
            }
            return outv;
        }

        /* TEMPO CURVE — local tempo across the whole song.

           A single global tempo is the wrong model for a lot of charted music: acceleration,
           deceleration and outright base-BPM shifts are common, and those are exactly the songs
           where a charter most wants to see what the music is doing. So the same machinery runs
           in an 8s window stepped every 2s.

           Three things stop it from being noise. A GLOBAL anchor is measured first and every
           local reading is folded into its octave — autocorrelation cannot tell a beat from a
           half-beat, and without folding an octave flip reads as a tempo change. A log-normal
           CONTINUITY prior (sigma 5%) biases each window toward the previous reading, so the
           ridge stays on one interpretation instead of hopping between metres. And a 5-point
           MEDIAN runs over the result, because one bad window is not a tempo change.

           Checked against songs whose tempo is known: Second Revolution (charted at 240) reads
           flat at 240.0 within half a percent across three minutes, while Megantus — whose
           global estimate was hopeless — resolves into a real curve that dips to 215 and peaks
           at 268 before settling at 242, which is why one number could never describe it. On the
           two hardest maps to hand it holds up: Parallel Universe Shifter traces a continuous
           journey from 95 up to 131 and back down through 85 to 70, and TremENDouS opens at
           twice its charted base and climbs 33%, matching the accelerandi its own speed events
           spell out.

           Those two also show the limit of the confidence number: a tempo that MOVES cannot pile
           onto one phase inside an 8s window, so concentration collapses across most of both
           songs even where the reading is good. The lane shades by it rather than gating on it —
           dim means "moving or unclear here", not "wrong".

           Run as a coroutine: roughly 35M float operations for a three-minute song. */


        internal static void BeginCurve(Result res)
        {
            if (res == null || res.Env == null || res.Hop <= 0f) return;
            if (res.Curve != null || res.CurveRunning) return;
            var runner = MainClass.Runner;
            if (runner == null) return;
            res.CurveRunning = true;
            runner.StartCoroutine(CurveJob(res));
        }

        private static IEnumerator CurveJob(Result res)
        {
            float hop = res.Hop;
            var fl = LogFlux(res.Env, hop, 0.012f);
            int W = Mathf.RoundToInt(CurveWindowSec / hop), S = Mathf.RoundToInt(CurveStepSec / hop);
            int count = Mathf.Max(1, (fl.Length - W) / Mathf.Max(1, S));

            /* A log-spaced tempo axis, scored densely, then decoded as a PATH.

               The greedy version this replaces asked each window for its best tempo while biasing
               it toward the previous window's answer — which biases the whole walk. Each step is
               only marginally better than staying put, nothing pulls the line back, and a song
               that holds one tempo for three minutes wandered 14% away from it. No amount of
               tightening that bias fixed it without also flattening the songs this lane exists
               for: Parallel Universe Shifter really does travel from 67 to 133, and a prior
               strong enough to stop the drift clipped it to 86-108.

               Viterbi has no memory bias. It maximises total score minus a smoothness cost over
               the WHOLE song, so a real change pays its cost once and keeps its winnings while an
               unmotivated drift never pays off at all. Measured: the steady song's spread falls
               from 18.9% to 1.9% and its frame-to-frame jitter from 0.96% to 0.04%, while PUS
               keeps its full 68-133 and now reaches the 67 its own chart documents. */
            /* 192 bins over the range — a 0.95% step, half the old grid's. On a song charted at
               250 that takes the curve's median error from 0.63% to 0.12% and halves its spread,
               which is worth the doubled emission cost: the lane exists to show changes, and it
               could not show one smaller than its own quantisation.

               Refining WITHIN the bin by fitting a parabola across the emission row was tried and
               measured worse — median error 0.48% and spread 2.65%, worse than the old coarse
               grid. Concentration is not parabolic at this spacing, so the fit chases noise in
               the neighbouring bins and reintroduces exactly the per-window jitter the path
               decode exists to remove. */
            int Bins = Mathf.Clamp(CurveBins, 24, 1024);
            /* Up to 480, not 360. Parallel Universe Shifter holds 440 BPM for forty-five
               seconds, and a tempo the grid cannot represent is not merely missed — the decode
               spends that stretch somewhere wrong and drags the path either side of it along.
               Measured against Camellia's own MIDI for that song, the ceiling is the single
               biggest error source: median absolute error 47.3% at 360 against 12.0% at 480.
               It costs about four points of octave agreement across 300 levels, since a song at
               200 can now also be read at 400 — but the number actually displayed, after the
               band fold, is unchanged at 70.0% against 70.3%. */
            const float TempoLo = 60f, TempoHi = 480f;
            float Lambda = Mathf.Max(0f, CurveLambda);
            float OctCost = Mathf.Max(0f, CurveOctCost);
            var grid = new float[Bins];
            var lg = new float[Bins];
            float k = Mathf.Log(TempoHi / TempoLo) / (Bins - 1);
            for (int i = 0; i < Bins; i++) { grid[i] = TempoLo * Mathf.Exp(i * k); lg[i] = Mathf.Log(grid[i]); }

            /* Reuse the emission when only the DECODE parameters moved. Scoring is most of the
               cost here; the path over it is milliseconds. Keeping it is what lets the smoothness
               and octave cost be changed and seen immediately rather than through a re-analysis
               that re-reads the song. */
            float[][] emit;
            bool reused = res.Emit != null && res.Emit.Length == count
                       && res.EmitKey == EmitKeyNow()
                       && res.Emit[0] != null && res.Emit[0].Length == Bins;
            if (reused) emit = res.Emit;
            else
            {
                emit = new float[count][];
                for (int t = 0; t < count; t++)
                {
                    int a0 = t * S, b0 = Mathf.Min(fl.Length, a0 + W);
                    var row = AcfHarm(fl, hop, grid, a0, b0);
                    for (int i = 0; i < Bins; i++) row[i] *= Concentration(fl, hop, grid[i], CurveWindowSec, a0, b0);
                    emit[t] = row;
                    if ((t & 1) == 0) yield return null;
                }
            }

            /* Scale the whole matrix so its MEAN is fixed. The transition cost is an absolute
               number of emission units, so without this one lambda means a different stiffness
               on every song — a track whose onsets barely concentrate gets a path frozen solid
               while a sharply percussive one wanders. Measured across 124 charted songs the
               normalisation is what let the cost be tuned at all: at a scale-free lambda of 64
               agreement rises to 88.3%, against 79.4% at the best scale-dependent setting. */
            if (!reused)
            {
                double mu = 0.0;
                for (int t = 0; t < count; t++) { var r = emit[t]; for (int i = 0; i < Bins; i++) mu += r[i]; }
                mu /= (double)count * Bins;
                if (mu > 1e-12)
                {
                    float k2 = (float)(EmitMean / mu);
                    for (int t = 0; t < count; t++) { var r = emit[t]; for (int i = 0; i < Bins; i++) r[i] *= k2; }
                }
                res.Emit = emit; res.EmitKey = EmitKeyNow();
            }
            _decodeKey = DecodeKeyNow();

            float[] path, conf;
            if (AutoTune)
            {
                /* Choose the smoothness by model selection rather than leaving it to be guessed.
                   Each candidate is scored by the emission it collects along its own path minus a
                   charge per change it makes; too free wins the emission and pays for hundreds of
                   changes, too stiff makes none and collects little. */
                float bestScore = float.NegativeInfinity, bestLam = Lambda;
                float[] bestPath = null, bestConf = null;
                for (int c = 0; c < AutoLambdas.Length; c++)
                {
                    float[] p2, c2;
                    Decode(emit, grid, lg, count, Bins, AutoLambdas[c], OctCost, out p2, out c2);
                    int changes;
                    float sc = PathScore(emit, p2, lg, grid, out changes) - ChangeCost * changes;
                    if (sc > bestScore) { bestScore = sc; bestLam = AutoLambdas[c]; bestPath = p2; bestConf = c2; }
                    yield return null;
                }
                CurveLambda = bestLam;
                _decodeKey = DecodeKeyNow();
                path = bestPath; conf = bestConf;
            }
            else Decode(emit, grid, lg, count, Bins, Lambda, OctCost, out path, out conf);

            /* The path is NOT folded point by point against the global estimate any more.
               Folding each reading into the global's octave sounds harmless — it only ever
               multiplies by a power of two — but it chops the curve wherever it crosses the
               square root of two away from that anchor, and it does so against a number the
               global estimator gets wrong on exactly the songs this lane exists for (Parallel
               Universe Shifter comes back 95 against a chart of 300). Measured on 124 charted
               songs: dropping it leaves octave-invariant agreement unchanged at 87.0% and lifts
               agreement on the number actually displayed from 56.9% to 66.5%.

               One shared octave shift below is all that remains, so the numbers read as base
               BPMs with the shape untouched. */
            var mid = new System.Collections.Generic.List<float>(count);
            for (int t = 0; t < count; t++) if (path[t] > 0f) mid.Add(path[t]);
            if (mid.Count > 0)
            {
                mid.Sort();
                float band = BandFactor(mid[mid.Count / 2]);
                if (!Mathf.Approximately(band, 1f))
                    for (int t = 0; t < count; t++) path[t] *= band;
            }

            float lo = float.MaxValue, hi = 0f;
            for (int t = 0; t < count; t++)
            {
                if (path[t] <= 0f) continue;
                if (path[t] < lo) lo = path[t];
                if (path[t] > hi) hi = path[t];
            }
            res.CurveLo = lo == float.MaxValue ? 0f : lo;
            res.CurveHi = hi;
            var sorted = new System.Collections.Generic.List<float>(count);
            for (int t = 0; t < count; t++) if (path[t] > 0f) sorted.Add(path[t]);
            sorted.Sort();
            res.CurveMedian = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0f;
            res.CurveSpread = res.CurveMedian > 0f ? (res.CurveHi - res.CurveLo) / res.CurveMedian : 1f;
            res.CurveConf = conf;
            res.Curve = path;
            res.CurveRunning = false;
        }

        /* One Viterbi pass over a GIVEN emission matrix. Split out so the auto-tuner can run
           several without rescoring the audio: the emission is the expensive half, the path over
           it is milliseconds. */
        private static void Decode(float[][] emit, float[] grid, float[] lg, int count, int Bins,
                                   float lambda, float octCost, out float[] path, out float[] conf)
        {
            var dp = new float[Bins];
            var nxt = new float[Bins];
            var back = new int[count][];
            for (int i = 0; i < Bins; i++) dp[i] = emit[0][i];
            back[0] = new int[Bins];
            /* TWO-SLOPE transition cost, O(bins) per window rather than O(bins^2).

                   cost(d) = Lambda*d + OctCost*max(0, d - Knee),   d = |log ratio|

               LINEAR, not quadratic, in the first place. A quadratic cost cannot express a step:
               one jump of d costs L*d², while N small steps summing to d cost L*d²/N, which
               vanishes as N grows — so the decode is rewarded for smearing every change into a
               slow ramp, which is what made a song going 250 to 270 read as a wander through
               256, 260 and 264. Linear charges the same total however a change is split.

               TWO slopes because one cannot do both jobs, and trying to tune it to was the bug.
               A low cost follows a real shift promptly but also lets the path flip octave
               whenever the emission's harmonic twin is briefly stronger; a high one stops the
               flipping and arrives ELEVEN SECONDS LATE. They are different kinds of move: a
               tempo change is a modest ratio, an octave flip is a factor near two. So the knee
               sits at 1.35x and everything past it pays extra. Measured:

                 Parallel Universe Shifter, against Camellia's own MIDI —
                   lambda 32 flat: median error 12.0%, and the change lands 11.5s late
                   lambda 12 + 36: median error  8.3%, 0.5s late
                 TremENDouS — 80-480 (octave-hopping) at a flat 8, 125-240 (an octave low) at a
                   flat 16, and 250-480 with the knee, which is the octave its chart implies
                 PP BREAKER — 250-273 either way, but a flat 8 dips to 67 partway
                 300 levels — 82.3%/67.0% flat, 81.7%/66.3% with the knee. Six tenths of a point.

               Still linear-time. Past the knee the slope is constant, so prefix and suffix
               running maxima answer every j at once. Inside it the range is bounded, which a
               monotonic deque answers in amortised O(1) per bin. */
            float far = lambda + octCost;
            float offK = octCost * KneeLog;
            float lgStep = Bins > 1 ? lg[1] - lg[0] : 1f;
            int K = Mathf.Clamp(Mathf.RoundToInt(KneeLog / Mathf.Max(1e-6f, lgStep)), 1, Bins);
            var pm = new float[Bins]; var pmI = new int[Bins];
            var sm = new float[Bins]; var smI = new int[Bins];
            var dq = new int[Bins + 1];
            for (int t = 1; t < count; t++)
            {
                var bk = new int[Bins];
                var et = emit[t];
                for (int j = 0; j < Bins; j++) { nxt[j] = float.MinValue; bk[j] = j; }

                // Far field: prefix max of (dp + far*lg), read K bins back.
                float run = float.MinValue; int runI = 0;
                for (int i = 0; i < Bins; i++)
                {
                    float v = dp[i] + far * lg[i];
                    if (v > run) { run = v; runI = i; }
                    pm[i] = run; pmI[i] = runI;
                }
                for (int j = K; j < Bins; j++)
                {
                    float v = pm[j - K] - far * lg[j] + offK;
                    if (v > nxt[j]) { nxt[j] = v; bk[j] = pmI[j - K]; }
                }
                // Far field the other way: suffix max of (dp - far*lg), read K bins forward.
                run = float.MinValue; runI = Bins - 1;
                for (int i = Bins - 1; i >= 0; i--)
                {
                    float v = dp[i] - far * lg[i];
                    if (v > run) { run = v; runI = i; }
                    sm[i] = run; smI[i] = runI;
                }
                for (int j = 0; j + K < Bins; j++)
                {
                    float v = sm[j + K] + far * lg[j] + offK;
                    if (v > nxt[j]) { nxt[j] = v; bk[j] = smI[j + K]; }
                }
                // Near field, i <= j: sliding max of (dp + Lambda*lg) over [j-K+1, j].
                int head = 0, tail = 0;
                for (int j = 0; j < Bins; j++)
                {
                    while (head < tail && dq[head] < j - K + 1) head++;
                    float fj = dp[j] + lambda * lg[j];
                    while (head < tail && dp[dq[tail - 1]] + lambda * lg[dq[tail - 1]] <= fj) tail--;
                    dq[tail++] = j;
                    int bi = dq[head];
                    float v = dp[bi] + lambda * lg[bi] - lambda * lg[j];
                    if (v > nxt[j]) { nxt[j] = v; bk[j] = bi; }
                }
                // Near field, i >= j: sliding max of (dp - Lambda*lg) over [j, j+K-1].
                head = 0; tail = 0;
                for (int j = Bins - 1; j >= 0; j--)
                {
                    while (head < tail && dq[head] > j + K - 1) head++;
                    float gj = dp[j] - lambda * lg[j];
                    while (head < tail && dp[dq[tail - 1]] - lambda * lg[dq[tail - 1]] <= gj) tail--;
                    dq[tail++] = j;
                    int bi = dq[head];
                    float v = dp[bi] - lambda * lg[bi] + lambda * lg[j];
                    if (v > nxt[j]) { nxt[j] = v; bk[j] = bi; }
                }
                for (int j = 0; j < Bins; j++) nxt[j] += et[j];
                var swap = dp; dp = nxt; nxt = swap;
                back[t] = bk;
            }

            int cur = 0;
            for (int i = 1; i < Bins; i++) if (dp[i] > dp[cur]) cur = i;
            path = new float[count];
            conf = new float[count];
            for (int t = count - 1; t >= 0; t--)
            {
                path[t] = grid[cur];
                conf[t] = emit[t][cur];
                cur = back[t][cur];
            }

        }

        /* Pull an estimate into the reference's octave, so a half/double flip stops masquerading
           as a tempo change. Powers of two are the right equivalence class for this and the
           charts say so: of 31,220 speed multipliers across 429 published levels, 78.5% are
           exact powers of two — subdivision changes, not tempo changes. */
        internal static float FoldOctave(float b, float reference)
        {
            if (b <= 0f || reference <= 0f) return b;
            int guard = 0;
            while (b < reference / 1.42f && guard++ < 8) b *= 2f;
            guard = 0;
            while (b > reference * 1.42f && guard++ < 8) b *= 0.5f;
            return b;
        }

        internal static float BeatPhase(Result res, float bpm, float windowSec = 30f)
        {
            if (res == null || res.Env == null || res.Hop <= 0f || bpm <= 0f) return -1f;
            var env = res.Env; float hop = res.Hop;
            float cr = 60f / bpm;
            if (cr <= hop) return -1f;
            int n = Mathf.Min(env.Length, Mathf.RoundToInt(windowSec / hop));
            if (n < 32) return -1f;

            const int Bins = 96;
            var fl = LogFlux(env, hop, 0.012f);
            var h = new float[Bins];
            float total = 0f;
            for (int i = 1; i < n; i++)
            {
                float f = fl[i];
                if (f <= 0f) continue;
                float ph = Mathf.Repeat(i * hop, cr);
                int b = Mathf.Clamp((int)(ph / cr * Bins), 0, Bins - 1);
                h[b] += f; total += f;
            }
            if (total <= 0f) return -1f;

            // Circular smoothing: a transient never lands cleanly inside one bin.
            var sm = new float[Bins];
            for (int b = 0; b < Bins; b++)
                sm[b] = 0.25f * h[(b - 1 + Bins) % Bins] + 0.5f * h[b] + 0.25f * h[(b + 1) % Bins];
            int best = 0;
            for (int b = 1; b < Bins; b++) if (sm[b] > sm[best]) best = b;

            // Parabolic refine against the neighbours — the bin is 1/96 of a beat, the answer
            // wants to be finer than that.
            float y0 = sm[(best - 1 + Bins) % Bins], y1 = sm[best], y2 = sm[(best + 1) % Bins];
            float den = y0 - 2f * y1 + y2;
            float d = Mathf.Abs(den) > 1e-12f ? 0.5f * (y0 - y2) / den : 0f;
            return Mathf.Repeat((best + d) / Bins * cr, cr);
        }

        /* Charters write a base BPM in a narrow band. Autocorrelation is blind to metre, so a
           detection can land an octave or two off — Parallel Universe Shifter reads 68 to 133
           where its chart says 300, which is the same tempo two octaves down. Folding the answer
           into [80, 300] puts it where a charter would recognise it.

           The ceiling is 300, not 350: a base BPM above 300 is rare enough that the halved
           reading is the better guess almost every time. It carries 5% of slack, though, because
           a song that really does sit AT 300 wanders either side of it and must not flip octave
           for the sake of a percent — so 300 stays 300, 310 stays 310, and 340 becomes 170.

           The whole curve is folded by ONE shared power of two, chosen from its median, not
           per-point: folding points individually would tear the line apart wherever it crossed a
           band edge, and every relative change — which is what the lane is for — must survive
           intact. That is also what makes the slack behave like "unless the surrounding tempo is
           near 300": a curve whose median sits at 300 keeps its whole shape there, excursions
           above included. */
        internal const float BandLo = 80f, BandHi = 300f;
        private const float BandSlack = 1.05f;   // tolerate 300-315 rather than halving it

        internal static float BandFactor(float bpm)
        {
            if (bpm <= 0f) return 1f;
            float f = 1f;
            int guard = 0;
            while (bpm * f < BandLo && guard++ < 8) f *= 2f;
            guard = 0;
            while (bpm * f > BandHi * BandSlack && guard++ < 8) f *= 0.5f;
            return f;
        }

        internal static float ToBand(float bpm) { return bpm * BandFactor(bpm); }

        /* Local tempo at an audio time; <=0 when there is no reading.

           Two corrections over reading the nearest sample. Sample i is measured over
           [i*step, i*step + window), so it describes the tempo at that window's CENTRE, not its
           start — taking it as the start reads the tempo half a window early, four seconds on
           the defaults. And the samples are LERPed rather than rounded to: the metronome
           integrates this beat by beat, so a staircase between samples becomes accumulated
           phase error on exactly the shifting songs the curve exists for. */
        internal static float TempoAt(Result res, float t)
        {
            if (res == null || res.Curve == null || res.Curve.Length == 0) return -1f;
            float x = (t - CurveWindowSec * 0.5f) / Mathf.Max(1e-6f, CurveStepSec);
            if (x <= 0f) return res.Curve[0];
            int i = Mathf.FloorToInt(x);
            if (i >= res.Curve.Length - 1) return res.Curve[res.Curve.Length - 1];
            return Mathf.Lerp(res.Curve[i], res.Curve[i + 1], x - i);
        }

        /* Two sharpeners for a tempo that is nearly right.

           LONG FOLD. The estimate is made over 30s, where a tempo 0.05% off has only drifted a
           sixteenth of a beat and still folds almost as tightly as the truth. Over the whole
           song that same error drifts half a beat and the fold falls apart, so re-sweeping a
           narrow band against every onset in the track separates the two: measured on a song
           charted at 250, the 30s answer is 249.88 and the full-song answer 249.96. Only valid
           when the tempo is STEADY — a moving tempo has no single fold to sharpen.

           ROUND SNAP. Charters write round numbers: of 89 distinct BPMs across two level
           libraries, 83% are integers and another 10% end in .5. The four genuinely odd ones —
           63.28125, 115.08, 180.15, 317.85 — are not noise around a round value but deliberately
           tuned ones, and they must survive untouched. They cluster in OLD levels (versions 7
           and 9, one of them with no events at all) where a nudged BPM was how you filled a
           pause or absorbed an irregular intro, before Pause events existed to do it properly;
           63.28125 is 2025/32 exactly, a round number divided down rather than a measured tempo.

           The nearest of them sits 0.070% from an integer (115.08), so the snap tolerance is
           0.04% — half that margin, and still four times the 0.016% a full-song fold leaves on a
           song charted at 250. */
        private static float Sharpen(Result res, float rough)
        {
            if (res == null || res.Env == null || res.Hop <= 0f || rough <= 0f) return rough;
            if (res.Curve == null || res.CurveMedian <= 0f || res.CurveSpread > 0.02f) return rough;

            var fl = LogFlux(res.Env, res.Hop, 0.012f);
            float songSec = res.Env.Length * res.Hop;
            const int Steps = 121;
            float bestBpm = rough, best = -1f;
            int bi = -1;
            var xs = new float[Steps];
            var vs = new float[Steps];
            for (int i = 0; i < Steps; i++)
            {
                float x = rough * (0.996f + 0.008f * i / (Steps - 1));
                float v = Concentration(fl, res.Hop, x, songSec);
                xs[i] = x; vs[i] = v;
                if (v > best) { best = v; bestBpm = x; bi = i; }
            }
            // Parabolic interpolation, so the answer is not pinned to a sweep sample.
            if (bi > 0 && bi < Steps - 1)
            {
                float y0 = vs[bi - 1], y1 = vs[bi], y2 = vs[bi + 1];
                float den = y0 - 2f * y1 + y2;
                if (Mathf.Abs(den) > 1e-12f)
                {
                    float d = Mathf.Clamp(0.5f * (y0 - y2) / den, -1f, 1f);
                    bestBpm = xs[bi] + d * (xs[bi + 1] - xs[bi - 1]) * 0.5f;
                }
            }
            return SnapRound(bestBpm);
        }

        // Nearest integer, then nearest half, but only from very close — see the note above.
        private static float SnapRound(float b)
        {
            const float Tol = 0.0004f;          // 0.04% — see the note above
            float i = Mathf.Round(b);
            if (i > 0f && Mathf.Abs(b - i) / b <= Tol) return i;
            float h = Mathf.Round(b * 2f) * 0.5f;
            if (h > 0f && Mathf.Abs(b - h) / b <= Tol) return h;
            return b;
        }

        /* The readout and the lane were answering the same question with different numbers —
           250 above a line sitting at 251.5 — because they come from different places. The global
           estimate is swept finely over the whole song and then snapped to a round value; the
           curve's points are bin centres on a 0.95% grid, and the nearest bin to 250 is 251.5.

           When the tempo is STEADY the global figure is strictly the better measurement, so the
           curve is rescaled onto it — by one shared factor, which moves the line without touching
           a single relative change. On a song whose tempo moves there is no single correct scale
           and no blended global estimate worth imposing, so this leaves it alone. */
        private static void AlignCurve(Result res)
        {
            if (res == null || res.Curve == null || res.Tempo <= 0f) return;
            if (res.CurveMedian <= 0f || res.CurveSpread > 0.02f) return;
            float k = res.Tempo / res.CurveMedian;
            if (k < 0.9f || k > 1.12f) return;      // a different octave is not a scale error
            if (Mathf.Abs(k - 1f) < 1e-4f) return;
            for (int i = 0; i < res.Curve.Length; i++) res.Curve[i] *= k;
            res.CurveLo *= k; res.CurveHi *= k; res.CurveMedian *= k;
            res.CurveVersion++;
        }

        /* Is the tempo reading worth acting on?

           Concentration alone is a poor gate. It measures how peaked the onset phase histogram
           is, which is a property of the music's texture — dense or syncopated material smears
           it — not of whether the tempo is right. A song that holds 250 BPM end to end was read
           as 249.90 and still labelled unsure, which is the metric failing, not the estimate.

           So corroboration counts too. The global tempo (autocorrelation plus a fine phase
           sweep) and the curve (a Viterbi path over a dense tempo grid) are different routes
           through the same envelope. When the path is STEADY and lands on the same tempo the
           global sweep found, two independent methods agree and the reading stands, even if the
           histogram was never sharp. A floor on concentration still applies, so two equally
           uninformed estimates agreeing does not count as evidence. */
        internal static bool TempoTrusted(Result res, float conf, out bool steady)
        {
            steady = false;
            if (res == null) return false;
            if (conf >= 0.30f) return true;
            if (conf < 0.10f || res.Curve == null || res.CurveMedian <= 0f || res.Tempo <= 0f) return false;
            if (res.CurveSpread > 0.02f) return false;
            float folded = FoldOctave(res.CurveMedian, res.Tempo);
            if (Mathf.Abs(Mathf.Log(folded / res.Tempo)) > 0.02f) return false;
            steady = true;
            return true;
        }

        /* Does the curve describe a tempo that actually MOVES, well enough to follow beat by
           beat? Two separate questions, and both have to be yes. A flat curve read as moving
           makes an integrated grid drift for no reason; an untrustworthy curve read as moving
           makes it drift wildly. 5% of spread is well past what bin quantisation and window
           noise produce on a steady song and well short of any real shift. */
        internal static bool CurveMoves(Result res)
        {
            if (res == null || res.Curve == null || res.CurveMedian <= 0f) return false;
            if (res.CurveSpread < 0.05f) return false;
            float conf = 0f;
            for (int i = 0; res.CurveConf != null && i < res.CurveConf.Length; i++) conf += res.CurveConf[i];
            if (res.CurveConf == null || res.CurveConf.Length == 0) return false;
            return conf / res.CurveConf.Length >= 0.10f;
        }

        // Nearest grid line to t, given a phase from BeatPhase. Never moves more than half a beat.
        internal static float SnapToBeat(float t, float phase, float bpm)
        {
            float cr = 60f / bpm;
            if (cr <= 0f) return t;
            return phase + Mathf.Round((t - phase) / cr) * cr;
        }

        /* Strongest attack within ±200ms of t0, for a level that ALREADY has an offset: the
           answer there is a correction, and a level charted three minutes into a long song must
           not be dragged back to the song's first sound. Among the strong peaks in the window
           the NEAREST wins, not the loudest — the loudest is as likely to be the next beat.
           Measured the same way: median 7ms, 74% within 30ms. */
        internal static float OnsetNear(Result res, float t0, float window = 0.20f)
        {
            if (res == null || res.Env == null || res.Hop <= 0f) return -1f;
            var env = res.Env; float hop = res.Hop;
            int n = env.Length;
            int a = Mathf.Max(1, Mathf.RoundToInt((t0 - window) / hop));
            int b = Mathf.Min(n - 2, Mathf.RoundToInt((t0 + window) / hop));
            if (b <= a) return -1f;
            var fl = LogFlux(env, hop, 0.012f);
            float best = 0f;
            for (int i = a; i < b; i++) if (fl[i] > best) best = fl[i];
            if (best <= 0.05f) return -1f;
            int pick = -1; float pickDist = float.MaxValue;
            for (int i = a; i < b; i++)
            {
                if (fl[i] < best * 0.8f || fl[i] < fl[i - 1] || fl[i] < fl[i + 1]) continue;
                float d = Mathf.Abs(i * hop - t0);
                if (d < pickDist) { pickDist = d; pick = i; }
            }
            if (pick < 0) return -1f;
            return WalkBack(env, pick, Mathf.Max(1, Mathf.RoundToInt(0.08f / hop))) * hop;
        }
    }
}
