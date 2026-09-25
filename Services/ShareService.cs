using System;
using System.Linq;
using System.Text;
using OfflineWinDict.Models;
using OfflineWinDict.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Metadata;
using WinRT.Interop;

namespace OfflineWinDict.Services
{
    /// <summary>Formats entries as shareable text; copies to clipboard and opens the Windows share sheet.</summary>
    public static class ShareService
    {
        public static string FormatEntry(WordEntry entry)
        {
            var sb = new StringBuilder();
            var dictName = DictionaryCatalog.ByCode(entry.LanguageCode).Name;
            sb.AppendLine($"{entry.Word}  ({dictName})");
            if (!string.IsNullOrWhiteSpace(entry.PhoneticText)) sb.AppendLine(entry.PhoneticText);
            sb.AppendLine();

            foreach (var group in entry.Meanings)
            {
                sb.AppendLine(group.PartOfSpeech);
                for (var i = 0; i < group.Senses.Count; i++)
                {
                    var sense = group.Senses[i];
                    sb.AppendLine($"  {i + 1}. {sense.Definition}");
                    if (!string.IsNullOrWhiteSpace(sense.Example))
                    {
                        sb.AppendLine($"     \u201C{sense.Example}\u201D");
                    }
                }
                sb.AppendLine();
            }

            if (entry.Synonyms.Count > 0)
            {
                sb.AppendLine($"Synonyms: {string.Join(", ", entry.Synonyms.Take(20))}");
            }
            if (entry.Antonyms.Count > 0)
            {
                sb.AppendLine($"Antonyms: {string.Join(", ", entry.Antonyms.Take(20))}");
            }
            if (!string.IsNullOrWhiteSpace(entry.Origin))
            {
                sb.AppendLine();
                sb.AppendLine($"Origin: {entry.Origin}");
            }

            sb.AppendLine();
            sb.Append($"Source: {entry.SourceName}");
            if (!string.IsNullOrWhiteSpace(entry.LicenseName)) sb.Append($" ({entry.LicenseName})");
            if (entry.SourceUrls.Count > 0) sb.Append($" — {entry.SourceUrls[0]}");
            return sb.ToString();
        }

        /// <summary>Copies text to the clipboard; returns false when the clipboard is locked.</summary>
        public static bool CopyToClipboard(string text)
        {
            try
            {
                var package = new DataPackage();
                package.RequestedOperation = DataPackageOperation.Copy;
                package.SetText(text);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Opens the Windows share sheet for the entry. Returns false when sharing is unavailable; callers fall back to the clipboard.</summary>
        public static bool TryShowShare(Microsoft.UI.Xaml.Window window, WordEntry entry)
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                var dtmType = typeof(DataTransferManager);

                // GetForWindow is a desktop-only factory member that is not present on every
                // TFM's projection surface; resolve it reflectively and bail out when missing.
                var getForWindow = dtmType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetForWindow" && m.GetParameters().Length == 1);
                if (getForWindow == null) return false;

                object arg = getForWindow.GetParameters()[0].ParameterType == typeof(IntPtr)
                    ? hwnd
                    : hwnd.ToInt64();
                if (getForWindow.Invoke(null, new[] { arg }) is not DataTransferManager manager) return false;

                manager.DataRequested += (sender, args) =>
                {
                    try
                    {
                        var dictName = DictionaryCatalog.ByCode(entry.LanguageCode).Name;
                        args.Request.Data.Properties.Title = $"{entry.Word} — {dictName} definition";
                        args.Request.Data.Properties.Description = "Shared from OfflineWinDict";
                        args.Request.Data.SetText(FormatEntry(entry));
                    }
                    catch
                    {
                        args.Request.FailWithDisplayText("This entry could not be shared.");
                    }
                };

                // Prefer the desktop-windowed ShowShareUIForWindow when present.
                var showForWindow = dtmType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "ShowShareUIForWindow" && m.GetParameters().Length == 1);
                if (showForWindow != null)
                {
                    object showArg = showForWindow.GetParameters()[0].ParameterType == typeof(IntPtr) ? hwnd : hwnd.ToInt64();
                    showForWindow.Invoke(null, new[] { showArg });
                }
                else
                {
                    DataTransferManager.ShowShareUI();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
