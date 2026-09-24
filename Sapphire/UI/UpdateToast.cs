using PrismLib.UI;

namespace Sapphire.UI
{
    /* Sapphire's update toast: the card itself is PrismLib.UI.Toast, shared with Bismuth. What
       stays here is the part only Sapphire can answer — what UpdateService is doing, what the
       message says, and what clicking it does.

       PrismLib.UI ships in the mod folder rather than installing itself, because the UI half needs
       no shared instance across mods: each mod draws its own card and ToastStack keeps them from
       overlapping by GameObject name. So, unlike PrismBridge, nothing here needs an Available
       guard.

       State is polled rather than subscribed: UpdateService runs on a worker thread and Unity
       objects may only be touched from this one. */
    internal static class UpdateToast
    {
        private static Toast _toast;
        private static bool _checkedOnce;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            if (s == null) return;

            // One check per session, once the engine is actually up.
            if (!_checkedOnce && s.AutoCheckUpdates && UnityEngine.Time.frameCount > 120)
            {
                _checkedOnce = true;
                UpdateService.Check(false);
            }

            if (_toast == null)
            {
                _toast = new Toast("Sapphire");
                _toast.Theme.Text = Theme.Text;
                _toast.Theme.TextMuted = Theme.TextMuted;
                _toast.Theme.CloseHover = Theme.CloseHover;
            }
            _toast.Theme.Accent = Theme.Accent;     // re-skinned at runtime by ApplyAccent
            _toast.Set(Current());
            _toast.Tick();
        }

        // What SHOULD be on screen right now; null = nothing. Key doubles as the dismiss key, so
        // dismissing "update available" doesn't also suppress the later "installed" for the same
        // version.
        private static ToastContent Current()
        {
            switch (UpdateService.Status)
            {
                case UpdateStatus.Available:
                    var info = UpdateService.Available;
                    if (info == null) return null;
                    return new ToastContent
                    {
                        Key = "avail:" + info.Tag,
                        Title = Loc.T("Update available") + ": " + info.Tag,
                        Hint = !string.IsNullOrEmpty(info.Name) ? info.Name : Loc.T("Click to update"),
                        AutoHide = true,          // only the actionable card times out
                        OnClick = () => UpdateService.Install(),
                    };

                case UpdateStatus.Installing:
                    float p = UpdateService.Progress;
                    return new ToastContent
                    {
                        Key = "installing",
                        Title = Loc.T("Downloading update…"),
                        Hint = p >= 0f ? UnityEngine.Mathf.RoundToInt(p * 100f) + "%" : Loc.T("Please wait"),
                        Progress = p,
                    };

                case UpdateStatus.Installed:
                    return new ToastContent
                    {
                        Key = "installed:" + UpdateService.Message,
                        Title = Loc.T("Update installed"),
                        Hint = Loc.T("Restart the game to apply it"),
                        Width = 400f,
                    };

                case UpdateStatus.Failed:
                    // Silent when the failing check was the automatic startup one — being offline
                    // is not news. The message is in the key so a retry that fails differently
                    // re-shows instead of being swallowed as already-dismissed.
                    if (!UpdateService.AnnounceFailures) return null;
                    return new ToastContent
                    {
                        Key = "failed:" + UpdateService.Message,
                        Title = Loc.T("Update failed"),
                        Hint = UpdateService.Message,
                        OnClick = () => UpdateService.Check(true),
                    };

                default:
                    return null;
            }
        }

        internal static void Dispose()
        {
            if (_toast != null) _toast.Dispose();
            _toast = null;
            _checkedOnce = false;
        }
    }
}
