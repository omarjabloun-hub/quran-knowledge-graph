// Reference template: the entry point. See ../SKILL.md §2 and §5.

using Mosaik;
using Mosaik.Helpers;
using App.FrontEnd.Chat;
using App.FrontEnd.Dashboard;
using Tesserae;
using TNT;
using static TNT.T;

namespace App.FrontEnd
{
    internal static class AppApp
    {
        /// <summary>
        /// The work-list page. Registered in TWO places, and it has to be both:
        /// <see cref="AppSidebar"/> for the ways in, <see cref="Router"/> for the URL behind
        /// them — a sidebar entry whose route is not registered navigates to nothing.
        /// </summary>
        internal const string InboxRoute = "#/inbox";

        private static void Main()
        {
            Mosaik.Admin.LazyLoad();

            // Renderers are NOT registered here. App.AutoDiscoverViews reflects over the loaded
            // assemblies for non-abstract INodeRenderer implementations with a parameter-less
            // constructor, so every renderer is picked up simply by existing.
            App.Initialize(Configure, OnLoad).FireAndForget();
        }

        /// <summary>
        /// Runs before the app is built: settings and sidebar hooks. AppSettings / AppSidebar are
        /// SIBLINGS of App (with AppSidenav and AppRouting) — older packages nested them as
        /// App.DefaultSettings / App.Sidebar, with the same members.
        /// </summary>
        private static void Configure(AppSettings settings)
        {
            settings.HomeView              = state => new DashboardView(state);
            settings.AssistantView         = AppChatView.Create;
            settings.DisableOnboardingTips = true;

            // One entry per way of looking at the work, in the same order and under the same
            // names the home page's counter cards use — the cards and these are two ways to the
            // same lists, and a reader who clicks "Urgent" on the home page has to find
            // "Urgent" in the sidebar afterwards.
            AppSidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
            {
                if (mode != AppSidebar.Mode.Default) return;

                sidebar.AddContent(new SidebarSeparator("APP_WORK", "Work".t()));

                foreach (var segment in WorkLists.Segments)
                {
                    var captured = segment;

                    // captured.Icon is a UIcons value: the string overload of SidebarButton
                    // binds to an EMOJI, so a UIcons name passed as text renders as letters.
                    var item = new SidebarButton("APP_" + captured.Id.ToUpperInvariant(),
                                                 captured.Icon, captured.Label.t())
                        .OnClick(() => Router.Navigate(InboxView.RouteFor(captured.Id)));

                    // Per SEGMENT, not per route: testing whether the hash contains "#/inbox"
                    // would light all of them at once, since every one is the same page with a
                    // different query.
                    refresh.Add(() => item.IsSelected = InboxView.IsShowing(captured.Id));

                    sidebar.AddContent(item);
                }
            };
        }

        /// <summary>Runs after the app is built: route registration.</summary>
        private static void OnLoad()
        {
            // The view goes through App.ShowDefault: a handler that only builds it shows a blank
            // page. And the path is OURS — the product registers #/browse, #/search, #/chat-ai
            // and others itself, and a page registered on one of those never shows.
            Router.Register(InboxRoute, state => App.ShowDefault(new InboxView(state)));
        }
    }
}
