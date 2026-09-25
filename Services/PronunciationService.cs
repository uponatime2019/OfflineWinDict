using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;

namespace OfflineWinDict.Services
{
    /// <summary>
    /// Plays pronunciations: recorded audio from the dictionary source when available,
    /// otherwise (or on demand) synthesizes speech with the installed Windows voice
    /// matching the dictionary language.
    /// </summary>
    public static class PronunciationService
    {
        private static readonly MediaPlayer Player = CreatePlayer();

        /// <summary>Kept alive while playing: WinRT streams must outlive the MediaSource.</summary>
        private static SpeechSynthesisStream? _activeStream;

        /// <summary>Raised when playback could not be started, so the UI can explain why.</summary>
        public static event Action<string>? PlaybackFailed;

        public static bool IsPlaying => Player.PlaybackSession != null && Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        private static MediaPlayer CreatePlayer()
        {
            var player = new MediaPlayer();
            player.MediaFailed += (_, e) =>
            {
                try { RaiseFail("Pronunciation audio could not be played."); } catch { }
            };
            return player;
        }

        private static void RaiseFail(string message)
        {
            PlaybackFailed?.Invoke(message);
        }

        /// <summary>Plays the entry's pronunciation: recorded audio first unless TTS is preferred.</summary>
        public static void PlayEntry(WordEntry entry, bool preferTts, string? voiceId)
        {
            try
            {
                if (!preferTts && entry.AudioUrls.Count > 0)
                {
                    PlayUrl(entry.AudioUrls[0]);
                    return;
                }
                Speak(entry.Word, entry.LanguageCode, voiceId);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PronunciationService.PlayEntry");
                RaiseFail("Pronunciation is not available for this word.");
            }
        }

        /// <summary>Plays a recorded pronunciation file over HTTPS.</summary>
        public static void PlayUrl(string url)
        {
            try
            {
                _activeStream?.Dispose();
                _activeStream = null;
                Player.Source = MediaSource.CreateFromUri(new Uri(url, UriKind.Absolute));
                Player.Play();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PronunciationService.PlayUrl");
                RaiseFail("Pronunciation audio could not be played.");
            }
        }

        /// <summary>Speaks the word with a Windows voice matching the dictionary language.</summary>
        public static void Speak(string text, string languageCode, string? preferredVoiceId)
        {
            _ = SpeakAsync(text, languageCode, preferredVoiceId);
        }

        public static async Task SpeakAsync(string text, string languageCode, string? preferredVoiceId)
        {
            try
            {
                var voices = SpeechSynthesizer.AllVoices;
                if (voices == null || voices.Count == 0)
                {
                    RaiseFail("No speech voices are installed on this PC.");
                    return;
                }

                using var synthesizer = new SpeechSynthesizer();
                var voice = voices.FirstOrDefault(v => v.Id == preferredVoiceId);
                if (voice == null)
                {
                    var bcp47 = LanguageToBcp47(languageCode);
                    voice = voices.FirstOrDefault(v => string.Equals(v.Language, bcp47, StringComparison.OrdinalIgnoreCase))
                            ?? voices.FirstOrDefault(v => v.Language?.StartsWith(bcp47.Split('-')[0], StringComparison.OrdinalIgnoreCase) == true)
                            ?? voices.FirstOrDefault(v => v.Gender == VoiceGender.Female)
                            ?? voices[0];
                }

                if (voice != null)
                {
                    synthesizer.Voice = voice;
                }

                _activeStream?.Dispose();
                _activeStream = await synthesizer.SynthesizeTextToStreamAsync(text);
                Player.Source = MediaSource.CreateFromStream(_activeStream, _activeStream.ContentType);
                Player.Play();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PronunciationService.Speak");
                RaiseFail("Text-to-speech is not available for this language.");
            }
        }

        public static bool HasRecordedAudio(WordEntry entry) => entry.AudioUrls.Count > 0;

        /// <summary>Installed Windows voices, usable for the Settings picker.</summary>
        public static IReadOnlyList<VoiceInformation> InstalledVoices
        {
            get
            {
                try { return SpeechSynthesizer.AllVoices; }
                catch { return Array.Empty<VoiceInformation>(); }
            }
        }

        public static string LanguageToBcp47(string code) => code switch
        {
            "en" => "en-US",
            "es" => "es-ES",
            "fr" => "fr-FR",
            "de" => "de-DE",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "nl" => "nl-NL",
            "ru" => "ru-RU",
            "tr" => "tr-TR",
            "hi" => "hi-IN",
            "ar" => "ar-SA",
            "zh" => "zh-CN",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            _ => "en-US",
        };
    }
}
