using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Chat;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Dashboard;
using QuranKG.FrontEnd.Themes;
using Tesserae;
using TNT;
using static TNT.T;

namespace QuranKG.FrontEnd
{
    /// <summary>
    /// The application's entry point. It extends the workspace's own front end — search, the
    /// graph explorer, chat and the admin area come with it — replaces the home page with the
    /// pattern dashboard, and adds the Stories and themes page.
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

            App.Initialize(Configure, OnLoad).FireAndForget();
        }

        /// <summary>Runs before the app is built: settings and sidebar hooks.</summary>
        private static void Configure(AppSettings settings)
        {
            settings.HomeView              = state => new PatternDashboard(state);
            settings.AssistantView         = QuranChatView.Create;
            settings.DisableOnboardingTips = true;

            // A page of our own is a route AND a way in: the entry here, the route in OnLoad.
            AppSidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
            {
                if (mode != AppSidebar.Mode.Default) return;

                var themes = new SidebarButton("QKG_THEMES", UIcons.CircleNodes, "Stories and themes".t())
                    .OnClick(() => Router.Navigate(ThemesView.Route));

                refresh.Add(() => themes.IsSelected = ThemesView.IsShowing());
                sidebar.AddContent(themes);
            };
        }

        /// <summary>Runs after the app is built: our own routes. A view reaches the screen through App.ShowDefault.</summary>
        private static void OnLoad()
        {
            Router.Register(ThemesView.Route, state => App.ShowDefault(new ThemesView(state)));
        }
    }
}
