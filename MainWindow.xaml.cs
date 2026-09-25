using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using OfflineWinDict.Views;
using Windows.Graphics;

namespace OfflineWinDict
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        public ElementTheme CurrentActualTheme => RootGrid?.ActualTheme ?? ElementTheme.Default;
        public FrameworkElement? WindowRoot => RootGrid;

        /// <summary>Applies a Light/Dark/System theme to the window content.</summary>
        public void ApplyTheme(string theme)
        {
            ThemeService.Apply(theme, RootGrid);
            UpdateNavVisuals();
        }

        private sealed class NavDef
        {
            public string Id { get; }
            public string Title { get; }
            public string Glyph { get; }
            public Type PageType { get; }

            public NavDef(string id, string title, string glyph, Type pageType)
            {
                Id = id;
                Title = title;
                Glyph = glyph;
                PageType = pageType;
            }
        }

        private static readonly NavDef[] NavItems =
        {
            new("Home", "Home", "\uE80F", typeof(HomePage)),
            new("Favorites", "Favorites", "\uE734", typeof(FavoritesPage)),
            new("Recent", "Recent", "\uE81C", typeof(RecentPage)),
            new("Wotd", "Word of the Day", "\uE787", typeof(WordOfTheDayPage)),
            new("Topics", "Topics", "\uE8F1", typeof(TopicsPage)),
        };

        private static readonly NavDef SettingsItem = new("Settings", "Settings", "\uE713", typeof(SettingsPage));

        private readonly Dictionary<string, Button> _navButtons = new();
        private readonly Dictionary<string, TextBlock> _navLabels = new();
        private Rectangle? _navDivider;
        private bool _suppressNavEvents;
        private string _currentId = "";

        public MainWindow()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.InitializeComponent");
                throw;
            }

            Instance = this;

            try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.SetAppIcon"); }

            try { AppWindow.Resize(new SizeInt32(1280, 820)); } catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.Resize"); }

            // Load settings before building UI so the theme and dictionary start correct.
            try
            {
                var load = System.Threading.Tasks.Task.Run(AppSession.LoadAsync);
                load.Wait(1500);
            }
            catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.LoadSettings"); }

            try { ThemeService.Apply(AppSession.Settings.Theme, RootGrid); }
            catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.Theme"); }

            BuildNav();
            InitDictionaryCombo();
            _ = OfflineWinDict.Services.UserDataService.Instance.EnsureLoadedAsync();

            AppSession.DictionaryChanged += OnDictionaryChanged;

            RootGrid.ActualThemeChanged += (_, _) => UpdateNavVisuals();

            try { if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) { presenter.Maximize(); } } catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.Maximize"); }

            NavigateToInitialPage();
            AppLogger.LogAction("MainWindow_Ready");
        }

        private void BuildNav()
        {
            try
            {
                var panel = new StackPanel { Spacing = 2 };

                // Divider between brand/dictionary and nav items
                _navDivider = new Rectangle
                {
                    Height = 1,
                    Fill = ThemeHelper.GetBrush("NavDividerBrush", RootGrid),
                    Margin = new Thickness(0, 4, 0, 10),
                };
                panel.Children.Add(_navDivider);

                foreach (var item in NavItems)
                {
                    panel.Children.Add(CreateNavButton(item));
                }

                if (NavMainHost is StackPanel mainHost)
                {
                    mainHost.Children.Add(panel);
                }

                if (BottomNavPanel is StackPanel host)
                {
                    var settingsButton = CreateNavButton(SettingsItem);
                    host.Children.Add(settingsButton);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.BuildNav");
            }
        }

        private Button CreateNavButton(NavDef item)
        {
            var icon = new FontIcon
            {
                Glyph = item.Glyph,
                FontSize = 17,
                Width = 24,
                Foreground = ThemeHelper.GetBrush("NavTextBrush", RootGrid),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = item.Title,
                FontSize = 13.5,
                Foreground = ThemeHelper.GetBrush("NavTextBrush", RootGrid),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var content = new Grid
            {
                ColumnSpacing = 12,
            };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(label, 1);
            content.Children.Add(icon);
            content.Children.Add(label);

            var button = new Button
            {
                Content = content,
                Tag = item.Id,
                MinHeight = 40,
                Padding = new Thickness(12, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent(),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                UseSystemFocusVisuals = true,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, item.Title);
            ToolTipService.SetToolTip(button, item.Title);
            button.Click += NavButton_Click;

            _navButtons[item.Id] = button;
            _navLabels[item.Id] = label;
            return button;
        }

        private void InitDictionaryCombo()
        {
            try
            {
                NavDictionaryCombo.ItemsSource = new List<DictionaryDef>(DictionaryCatalog.All);
                NavDictionaryCombo.DisplayMemberPath = nameof(DictionaryDef.Name);
                NavDictionaryCombo.SelectedIndex = DictionaryCatalog.IndexOf(AppSession.DictionaryCode);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.InitDictionaryCombo");
            }
        }

        private void OnDictionaryChanged(string code)
        {
            // Sync the combo when the dictionary is changed elsewhere (e.g. Home page).
            try
            {
                var index = DictionaryCatalog.IndexOf(code);
                if (NavDictionaryCombo.SelectedIndex != index)
                {
                    _suppressNavEvents = true;
                    NavDictionaryCombo.SelectedIndex = index;
                    _suppressNavEvents = false;
                }
            }
            catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.OnDictionaryChanged"); }
        }

        private void NavDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_suppressNavEvents) return;
                if (NavDictionaryCombo.SelectedItem is DictionaryDef def)
                {
                    AppSession.DictionaryCode = def.Code;
                    AppLogger.LogAction("MainWindow_DictionaryChanged", new() { { "code", def.Code } });

                    if (ContentFrame.Content is Views.HomePage home)
                    {
                        home.ChangeDictionary(def.Code);
                    }
                }
            }
            catch (Exception ex) { AppLogger.LogException(ex, "MainWindow.DictionaryChanged"); }
        }

        private void NavigateToInitialPage()
        {
            var target = "Home";
            try
            {
                if (AppSession.Settings.RestoreLastPage && !string.IsNullOrWhiteSpace(AppSession.Settings.LastPage))
                {
                    target = AppSession.Settings.LastPage;
                }
            }
            catch { }
            if (!_navButtons.ContainsKey(target)) target = "Home";
            Navigate(target, savePreference: false);
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string id })
            {
                Navigate(id);
            }
        }

        /// <summary>Navigates the content frame to a named page.</summary>
        public void Navigate(string pageId, bool savePreference = true)
        {
            try
            {
                Type? pageType = pageId switch
                {
                    "Home" => typeof(HomePage),
                    "Favorites" => typeof(FavoritesPage),
                    "Recent" => typeof(RecentPage),
                    "Wotd" => typeof(WordOfTheDayPage),
                    "Topics" => typeof(TopicsPage),
                    "Settings" => typeof(SettingsPage),
                    _ => null,
                };
                if (pageType == null) return;

                if (_currentId != pageId || ContentFrame.Content?.GetType() != pageType)
                {
                    ContentFrame.Navigate(pageType);
                    _currentId = pageId;
                }

                UpdateNavVisuals();

                if (savePreference)
                {
                    AppSession.Settings.LastPage = pageId;
                    _ = AppSession.SaveSettingsAsync();
                }

                AppLogger.LogAction("MainWindow_Navigate", new() { { "page", pageId } });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.Navigate " + pageId);
            }
        }

        private void UpdateNavVisuals()
        {
            try
            {
                if (_navDivider != null)
                {
                    _navDivider.Fill = ThemeHelper.GetBrush("NavDividerBrush", RootGrid);
                }

                var navSelectedBrush = ThemeHelper.GetBrush("NavSelectedBrush", RootGrid);
                var navTextSelectedBrush = ThemeHelper.GetBrush("NavTextSelectedBrush", RootGrid);
                var navTextBrush = ThemeHelper.GetBrush("NavTextBrush", RootGrid);

                foreach (var (id, button) in _navButtons)
                {
                    var selected = id == _currentId;
                    button.Background = selected ? navSelectedBrush : Brushes.Transparent();
                    if (_navLabels.TryGetValue(id, out var label))
                    {
                        label.Foreground = selected ? navTextSelectedBrush : navTextBrush;
                        label.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                    }
                    if (button.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is FontIcon icon)
                    {
                        icon.Foreground = selected ? navTextSelectedBrush : navTextBrush;
                    }
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, _navLabels.TryGetValue(id, out var l2) ? l2.Text ?? id : id);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.UpdateNavVisuals");
            }
        }

        /// <summary>Opens a word in the Home detail panel from any page.</summary>
        public static void OpenWordInHome(string word, string languageCode)
        {
            try
            {
                Instance?.Navigate("Home");
                HomePage.Instance?.OpenEntry(word, languageCode);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.OpenWordInHome");
            }
        }
    }

    internal static class Brushes
    {
        private static Microsoft.UI.Xaml.Media.Brush? _transparent;
        public static Microsoft.UI.Xaml.Media.Brush Transparent() =>
            _transparent ??= new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
