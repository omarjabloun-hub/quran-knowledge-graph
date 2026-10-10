---
name: curiosity-frontend-setup
description: Setting up a Curiosity front-end project — the Transpose SDK project file, App.Initialize and the settings hook, the generated N/E schema constants, registering routes and sidebar entries, the per-session "who am I rendering as" state, and the Transpose constraints that shape every DTO. Use when creating the front-end project, adding a page of your own, or hitting a Transpose-specific compile or runtime problem.
---

# Front-end setup — Transpose, Tesserae, and the app shell

The front end is C# transpiled to JavaScript, rendered with Tesserae. No React, no
TypeScript. It extends the workspace's own front end rather than replacing it: you get
search, the graph, files, chat and admin for free, and you override the parts that are yours.

---

## 1. Project shape

```
src/<App>.FrontEnd/                netstandard2.0, Sdk="Transpose.Build.Target/<version>"
├── <App>.FrontEnd.csproj          Curiosity.FrontEnd(.Core/.API/.Admin) + Transpose.BCL + Tesserae
├── tps.json / tps.Release.json    bundle name, formatting, minification
└── src/
    ├── <App>App.cs                Main → App.Initialize(Configure, OnLoad)
    ├── Schema/N.cs                GENERATED — node types, field names, edges (§3)
    ├── Schema/PropertyLinks.cs    which properties are really references (see renderers skill)
    ├── Api/
    │   ├── Endpoints.cs           one const string per endpoint path, XML-documented
    │   ├── Dto.cs                 request/response DTOs — mirrored server-side
    │   └── Api.<App>.cs           one method per endpoint
    ├── Renderers/                 one per node type
    ├── Dashboard/                 the home view replacement, your own pages
    ├── Chat/                      a custom chat view, if you have one
    └── Components/                shared pieces
```

`<NoWarn>CS1998,CA2007</NoWarn>` — async-without-await and ConfigureAwait are noise in
transpiled code, and the Curiosity projects disable them too.

**Pin Tesserae to the same version the Curiosity packages were built against.** If you need a
component newer than that, a direct `PackageReference` *ahead* of it works as long as the
additions are additive — the pre-transpiled Curiosity modules keep working. Bump both
together when the packages catch up.

---

## 2. Entry point

```csharp
private static void Main()
{
    Mosaik.Admin.LazyLoad();
    App.Initialize(Configure, OnLoad).FireAndForget();
}

private static void Configure(AppSettings settings)
{
    settings.HomeView              = state => new DashboardView(state);   // your home page
    settings.AssistantView         = MyChatView.Create;                   // your chat page
    settings.DisableOnboardingTips = true;

    AppSidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) => { … };  // §5
}

private static void OnLoad()
{
    Router.Register(MyRoute, state => App.ShowDefault(new MyView(state)));
}
```

- `Configure` runs before the app is built: settings and sidebar hooks.
- `OnLoad` runs after: route registration.
- **The app shell is four sibling classes, not one nested one:** `AppSettings`, `AppSidebar`
  (with its nested `Mode`), `AppSidenav` and `AppRouting`, beside `App`. Older packages nested
  them as `App.DefaultSettings`, `App.Sidebar`, `App.Sidenav`, `App.DefaultRouting`; the
  properties and `OnSidebarRebuild_*` events are unchanged, so a build failing on one of those
  names is the rename and nothing else. `App.Initialize` and `App.ShowDefault` stay on `App`.
- **A route handler must hand its view to `App.ShowDefault(...)`.** A handler that returns a
  view, or builds one and does nothing with it, navigates to a blank page.
- **The product registers some routes itself** (`#/browse`, `#/chat-ai`, `#/search`, …) and
  wins: a page of yours registered on one of them opens the product's page instead. Give your
  pages their own paths.
- **Renderers are not registered anywhere.** Auto-discovery reflects over the loaded
  assemblies for every non-abstract `INodeRenderer` implementation and instantiates it — see
  `curiosity-node-renderers` §1 for the two rules that make that work.

---

## 3. `Schema/N.cs` is generated — never hand-edit it

`N` and `E` are the shared vocabulary of the connector, the endpoints and this project, so all
three spell every node type and field the same way.

```bash
dotnet run --project src/<App>.Connector -- --emit-frontend-schema src/<App>.FrontEnd/src/Schema/N.cs
```

Re-run after **any** change to a `[Node]` class or the edge catalogue. The workspace generates
the same shape server-side and injects it above every endpoint script, so `N.Case.Title` means
the same thing in all three places and a renamed property is a build error rather than an
empty label at run time.

Three rules keep the front end's `N` the same file the server injects:

- **Spell the graph-owned types the server's way** — `N._User`, `N._FileEntry`, and the edges
  `E._HasAttachment`, `E._Mentions`, `E._AppearsIn`. They have no `[Node]` class to reflect
  over, so the emitter lists them; a front-end-only spelling (`N.User`, `E.HasAttachment`)
  compiles here and fails in every endpoint that copies it.
- **Skip `[Timestamp]` properties.** On the server a `[Timestamp]` property *is* the node's
  `Timestamp`, not a field: `GetString(N.X.Released)` throws in an endpoint and reads nothing in
  a renderer. Read `node.Timestamp`.
- **Emit the useful sets into their own class** (`Groups.DocumentTypes`,
  `Groups.All360Types`), not onto `N`, so a new record type joins the generic views by existing.
  The workspace's generated `N` has no sets: `N.DocumentTypes` copied into an endpoint is the
  most common 26.10 compile error. Server code keeps its own list in the helper library.

---

## 4. Transpose constraints — read before writing any DTO

- **No runtime reflection.** You cannot enumerate an arbitrary object's properties. DTOs
  crossing the C#/JS boundary must be **flat**: no inheritance, no polymorphism, no nested
  generics beyond arrays. They are ordinary classes — the endpoint call deserialises them
  normally.
- **`UID128` is a JS string at run time**, and a struct with a string converter server-side.
  Identical on the wire either way, so use the type on both sides and get a compile error when
  a UID reaches the wrong parameter.
- **`Script.Write<T>("…", args)`** is the escape hatch into raw JS, with no type safety. Avoid.
- **Match the naming shortcuts** used throughout: `.WS()` width 100 %, `.HS()` height 100 %,
  `.S()` both, `.P(n)` padding, `.M(n)` margin, `.PB(4)` padding-bottom.
- **Namespace collisions are real.** More than one Curiosity namespace declares a
  `NeighborsLinks`; importing both makes every use ambiguous (CS0104). Bring the one you want
  in with a **using alias** rather than importing its whole namespace.
- **A clean build of the front-end entry project is not proof that a referenced project
  compiles under Transpose** — some entry apps consume referenced projects as
  already-transpiled modules and do not re-run the compiler over their sources. Build the
  entry app that re-transpiles everything.

Imports every file takes:

```csharp
using static Tesserae.UI;      // TextBlock, VStack, Card, SplitView, Defer, …
using static Mosaik.UI;        // SearchArea, NeighborsLinks, Metric, Banner, DetailsGrid, …
using static TNT.T;            // .t() — every user-visible string is translatable
```

Use the **factory functions**, not `new`: `SearchArea()`, `Card(...)`, `VStack()`.

---

## 5. Your own pages — register in two places

A page of your own is a route **and** a way in. Both, or the entry navigates to nothing:

```csharp
// Configure:
AppSidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
{
    if (mode != AppSidebar.Mode.Default) return;

    sidebar.AddContent(new SidebarSeparator("APP_WORK", "Work".t()));

    foreach (var segment in WorkLists.Segments)
    {
        var captured = segment;
        var item = new SidebarButton("APP_" + captured.Id.ToUpperInvariant(),
                                     captured.Icon, captured.Label.t())
            .OnClick(() => Router.Navigate(InboxView.RouteFor(captured.Id)));

        refresh.Add(() => item.IsSelected = InboxView.IsShowing(captured.Id));
        sidebar.AddContent(item);
    }
};

// OnLoad:
Router.Register(InboxRoute, state => App.ShowDefault(new InboxView(state)));
```

Three rules:

- **One page, several entries, distinguished by a query parameter.** Build the URL in exactly
  one place (`InboxView.RouteFor(segment)`), and let the view read the segment out of its
  `Parameters`. Every caller — sidebar, metric tile, banner — goes through that one method.
- **Selected state is per segment, not per route.** Testing whether the hash contains the route
  lights all the entries at once. Compare the query, and treat a bare route as the default
  segment — otherwise landing with no query lights nothing while showing something.
- **Put URL state in the hash query string**, through the product's route-state helpers, with
  a restore path in the route handler. A write with no restore produces URLs that lie after a
  refresh.

Note the second registration rule for admin-area pages if you add any: some route tables are
declared in two files (a key list and a handler list) and a route in only one silently fails
to navigate.

---

## 6. "Who am I rendering as"

A demo usually has no real login but still needs per-user views. Put the current viewer in
**one** place that every view reads:

```csharp
Viewer.EnsureAsync();     // the current user, choosing a default on first use
Viewer.UsersAsync();      // the list, fetched once per session
Viewer.Set(user);         // switch
Viewer.Changed;           // rebuild whatever is on screen
```

It starts as a field on the home page, and the moment a second view shows "my" work the two
disagree. Extract it the first time, not the second.

For the picker itself, prefer a **searchable list in a modal** over a dropdown when users
differ in ways worth showing — site, department, role, specialities. Have the endpoint return
each dimension **twice**: as UIDs (which is what renders, so a speciality opens its page) and
as text (which is what the client-side search matches). Nobody types a UID.

---

## 7. Verified names — what older code and doc pages get wrong

Checked against the `Curiosity.FrontEnd*` 26.10 sources and the reference pages Tesserae
2026.10 ships in its package (`.claude/skills/tesserae/references/`). When code or a doc page
uses the left column, the right column is what compiles.

| Wrong | Right |
| --- | --- |
| `App.DefaultSettings`, `App.Sidebar`, `App.Sidenav`, `App.DefaultRouting` | `AppSettings`, `AppSidebar` (`AppSidebar.Mode`), `AppSidenav`, `AppRouting` |
| `Mosaik.Schema.SimilarityResult` | `Mosaik.GraphDB.Similarity.SimilarityResult` (`Source`, `Scores`, `Timings`) |
| `SearchArea.WithSimilarityEngine(Func<Task<…>>)` | takes `Func<SearchRequest, Task<SimilarityResult>>` |
| `SearchArea.WithMultiSelect(uids => …)` | selection is a renderer concern: `.Renderer(r => r.WithSelection().OnSelected(rows => …))`; a row's UID is `row.SearchHit.Node.UID` |
| `SearchArea().NoSearchBox().OnSearch(…)` | never searches — `OnSearch` modifies the request a search box sends, and there is no box. Pass the request: `.NoSearchBox().SearchFor(new SearchRequest().WithTargetUIDs(…))` (or `WithTargetQuery`) |
| `Node.Preview(uid)` | `Mosaik.Components.NodePreview.For(uid, state = null, ignoreNotFound = false)`; also `CloseAll`, `SwitchTo`, `IsOpen`, `TryGetCurrent` |
| `RouteState` not found | it is in `Mosaik.Helpers` — a missing `using`, not a rename |
| `Parameters.Has(key)` | `ContainsKey(key)` (`Tesserae.Parameters`) |
| `DefaultRoutes.Assistant` | `DefaultRoutes.ChatAI.Base`, `DefaultRoutes.ChatAI.OpenChat(chatUID, messageUID)` |
| `ChatView(...)` as a factory | `new ChatView(ChatViewConfiguration, Parameters)` |
| `.WithWelcome(title, subtitle, prompts)` | the settings `WelcomeTitle` and `WelcomePrompts`; there is no subtitle — put the sentence in `ChatBoxPlaceholderText` |
| `.WithCorpusLinking()` | `ParseAs = new ChatViewParseAs { PipelineUID, NodeType, FieldName }` |
| `ChatAI.StartConversationAsync(q, attachments)` | `API.ChatAI.NewChatWithContext(providerUID, assistantUID, uids)`, then `API.ChatAI.PostMessage(chat.UID, question)` |
| `SchemaStyleInfo { Type = … }` | the member is `Name` (`Mosaik.Schema.SchemaStyleInfo`); `DisplayName` is the label |
| `Metric(value, label)` | `Metric(title, value)` — the number is the **second** argument; `.ValueFirst()` draws it on top |
| `Metric.SetIcon(new IconTile(i).Background(c))` | `Metric.SetIcon(UIcons icon, string color)`; a standalone tile is `IconTile(icon, color)` and re-tints with `.Tint(color)` |
| `Metric.ChangeInHeader("+3")` | `.Change(component).ChangeInHeader()` — `ChangeInHeader` takes a `bool` |
| `Nothing()` | `Empty()` |
| `Grow()` as a spacer | `Grow` is an extension; the spacer is `HStack().Grow()` |
| `Toast().Danger(msg)` | `Toast().Error(msg)` — Toast has `Success` / `Information` / `Warning` / `Error`; `Danger` is Badge and Banner vocabulary |
| `stack.OnClick(...)` | `Stack` has no click handler: wrap the row in `Card(...).OnClick(...)`, or use a `Button` |
| `badge.OnClick(() => …)` | most components take `ComponentEventHandler<T, MouseEvent>` → `(_, __) => …`; `Card`, `Button` and `Metric` also take an `Action` |
| `stack.Children(x)` in a loop | `Children` **replaces**; add one at a time with `stack.Add(x)` |
| `new SidebarButton(id, icon.ToString(), text)` | pass the `UIcons` value; the string overload binds to an emoji |
| `HTMLElement` not found | `using static Transpose.Core.dom;` |
| `UIcons.UserSwitch`, `UIcons.ExclamationCircle` | `UIcons.Users`, `UIcons.OctagonExclamation` |
| a `NodeTab` without `TabTitle` | the node view builds a segmented pivot from `TabTitle`; a tab without one throws `_titleCreator is not a function` and takes every node page down |

Two layout traps that compile and render nothing:

- **A `SearchArea` inside a scrolling page has no height** and shows "205 hits" over no rows.
  Give it an explicit height (or put it in a container that has one).
- **A list whose numbers come from your endpoint has to be checked against that endpoint.**
  The front-end DTO and the server DTO are declared twice (`curiosity-code-endpoints` §4); when
  they drift, the panel deserialises to nulls and shows empty, with no error. Open every screen
  with data behind it, not only the build.

**To check a name this table does not cover**, read the source (Tesserae's public repo, or the
reference pages the package installs), or load the `Curiosity.FrontEnd*` DLLs into a throwaway
`net10.0` console app with `System.Reflection.MetadataLoadContext` and list the public members.
Five minutes, and the only way to be sure — doc pages, and this table, lag the packages.

---

## 8. Checklist

- [ ] Tesserae version pinned against the Curiosity packages
- [ ] `N.cs` generated from the connector, committed, never hand-edited; `_User` / `_FileEntry` /
      `E._HasAttachment` spelled the server's way; no `[Timestamp]` fields; sets in `Groups`
- [ ] Every DTO flat; every identifier a `UID128`
- [ ] Every user-visible string wrapped in `.t()`
- [ ] `AppSettings` / `AppSidebar` / `AppSidenav` / `AppRouting`, not the nested `App.*` names
- [ ] Own pages registered as **both** a route and a sidebar entry, on paths the product does not own
- [ ] Route URLs built in exactly one method
- [ ] Viewer state in one place, read by every view
- [ ] Unused product features switched off in the workspace's interface settings

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`App.FrontEnd.csproj`](examples/App.FrontEnd.csproj) | The Transpose SDK, `netstandard2.0`, the Curiosity + Tesserae package set |
| [`tps.json`](examples/tps.json) | The bundle configuration |
| [`AppApp.cs`](examples/AppApp.cs) | `Main` / `Configure` / `OnLoad`, the home and chat overrides, sidebar entries and route registration |
| [`Api/Endpoints.cs`](examples/Api/Endpoints.cs) | One documented const per endpoint path |
| [`Api/Dto.cs`](examples/Api/Dto.cs) | Flat, UID-typed DTOs — the front-end half of the wire contract |
| [`Api/Api.cs`](examples/Api/Api.cs) | One method per endpoint, `readOnlyCompatible` mirrored from the server side |
