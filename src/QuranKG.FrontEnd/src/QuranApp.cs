using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Chat;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Dashboard;
using QuranKG.FrontEnd.Hadith;
using QuranKG.FrontEnd.Home;
using QuranKG.FrontEnd.Reader;
using QuranKG.FrontEnd.Themes;
using Tesserae;
using TNT;
using static TNT.T;

namespace QuranKG.FrontEnd
{
    /// <summary>
    /// The application's entry point. It extends the workspace's own front end — search, the
    /// graph explorer, chat and the admin area come with it — replaces the home page with the
    /// pattern dashboard, and adds the Quran View (#/read) and the Stories and themes page.
    ///
    /// <para>Renderers are not registered here: the app reflects over this assembly for every
    /// non-abstract INodeRenderer with a parameter-less constructor (src/Renderers/), so a renderer
    /// joins simply by existing.</para>
    /// </summary>
    internal static class QuranApp
    {
        private static void Main()
        {
            Mosaik.Admin.LazyLoad();

            // The Arabic typefaces are requested before the first page draws, so the first verse
            // on screen does not reflow when they arrive.
            Arabic.EnsureFonts();
            Look.EnsureStyles();

            App.Initialize(Configure, OnLoad).FireAndForget();
        }

        /// <summary>Runs before the app is built: settings and sidebar hooks.</summary>
        private static void Configure(AppSettings settings)
        {
            settings.HomeView              = state => new HomeView(state);
            settings.AssistantView         = QuranChatView.Create;
            settings.DisableOnboardingTips = true;

            // A page of our own is a route AND a way in: the entry here, the route in OnLoad.
            AppSidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
            {
                if (mode != AppSidebar.Mode.Default) return;

                var read = new SidebarButton("QKG_READ", UIcons.BookQuran, "Read the Quran".t())
                    .OnClick(() => Router.Navigate(QuranLinks.Route));

                var themes = new SidebarButton("QKG_THEMES", UIcons.CircleNodes, "Stories and themes".t())
                    .OnClick(() => Router.Navigate(ThemesView.Route));

                var hadith = new SidebarButton("QKG_HADITH", UIcons.CommentQuote, "Hadith and the Quran".t())
                    .OnClick(() => Router.Navigate(HadithStudyView.Route));

                var patterns = new SidebarButton("QKG_PATTERNS", UIcons.ChartHistogram, "Patterns".t())
                    .OnClick(() => Router.Navigate(PatternDashboard.Route));

                var tajweed = new SidebarButton("QKG_TAJWEED", UIcons.Palette, "Tajweed".t())
                    .OnClick(() => Router.Navigate(TajweedPage.TajweedView.Route));

                refresh.Add(() => read.IsSelected   = QuranView.IsShowing());
                refresh.Add(() => patterns.IsSelected = PatternDashboard.IsShowing());
                refresh.Add(() => themes.IsSelected = ThemesView.IsShowing());
                refresh.Add(() => hadith.IsSelected = HadithStudyView.IsShowing());
                refresh.Add(() => tajweed.IsSelected = TajweedPage.TajweedView.IsShowing());
                sidebar.AddContent(read);
                sidebar.AddContent(tajweed);
                sidebar.AddContent(themes);
                sidebar.AddContent(hadith);
                sidebar.AddContent(patterns);
            };
        }

        /// <summary>Runs after the app is built: our own routes. A view reaches the screen through App.ShowDefault.</summary>
        private static void OnLoad()
        {
            Router.Register(ThemesView.Route, state => App.ShowDefault(new ThemesView(state)));
            Router.Register(HadithStudyView.Route, state => App.ShowDefault(new HadithStudyView(state)));
            Router.Register(PatternDashboard.Route, state => App.ShowDefault(new PatternDashboard(state)));
            Router.Register(TajweedPage.TajweedView.Route, state => App.ShowDefault(new TajweedPage.TajweedView(state)));
            QuranView.Register();
        }
    }
}
