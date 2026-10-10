using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Dashboard;
using Tesserae;

namespace QuranKG.FrontEnd
{
    /// <summary>
    /// The application's entry point. It extends the workspace's own front end — search, the
    /// graph explorer, chat and the admin area come with it — and replaces the home page with the
    /// pattern dashboard.
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
            settings.DisableOnboardingTips = true;
        }

        /// <summary>Runs after the app is built: routes of our own would be registered here.</summary>
        private static void OnLoad()
        {
        }
    }
}
