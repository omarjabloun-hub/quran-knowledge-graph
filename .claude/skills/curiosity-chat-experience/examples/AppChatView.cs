// Reference template: a purpose-built chat page. Installed with settings.AssistantView.
// See ../SKILL.md §1–§4.
//
// The stock ChatView is a general-purpose chat: the user picks an assistant, then picks which
// of the workspace's tools and agents that assistant may use this turn. NEITHER CHOICE MAKES
// SENSE HERE. There is exactly one assistant worth talking to in this workspace — the one whose
// system prompt is the whole method — and the capabilities it needs are the same on every turn.
// A picker over that set can only be used to break it.
//
// So this view fixes both and hides both pickers.

using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using App.FrontEnd.Components;
using App.FrontEnd.Schema;
using TNT;
using UID;
using static TNT.T;

namespace App.FrontEnd.Chat
{
    internal static class AppChatView
    {
        // -------------------------------------------------------------------------------
        // The pinned capability set.
        //
        // These are the UIDs the assistant is configured with, RESTATED HERE so the page does
        // not depend on that configuration staying right: an assistant edited in the admin UI
        // to drop `consult` would otherwise silently turn every answer into a guess about
        // record titles.
        //
        // `consult` is a workspace BUILT-IN and its UID is stable across deployments. Search
        // Records and the sandbox are definitions this application ships under
        // code/chat-ai-tools with fixed, readable UIDs (see curiosity-ai-tools-and-agents §2 and
        // curiosity-sandboxes); the rest were randomly minted at creation.
        //
        // NOT pinned: the built-in `search` (LLMSearCh1111111111111) interprets the question
        // through an agent over the llm-search.json types and found nothing for exact
        // identifiers in every demo that tried it, and Search Files (AiTooLsEarCh1111111111)
        // searches FILES only — with a corpus of records the model confuses its ConsultFile
        // with consult.
        // -------------------------------------------------------------------------------

        /// <summary>Ours (fixed UID): the workspace search as the user, returning id, type and label.</summary>
        private static readonly UID128 Tool_Search   = new UID128("SEARCHRecords111111111");

        /// <summary>Built-in: reads any record the user can access, or its published file, by uid.</summary>
        private static readonly UID128 Tool_Consult  = new UID128("ConsuLt111111111111111");

        /// <summary>Ours (fixed UID): bash, jq and python over the corpus as files — the open-ended
        /// counting and joining question no fixed tool answers.</summary>
        private static readonly UID128 Tool_Sandbox  = new UID128("<sandbox uid>");

        /// <summary>Ours: ListInbox / ListUrgent / ListUnassigned, over the same assignment
        /// edges the home page counts — which is what keeps the assistant and the dashboard
        /// from disagreeing.</summary>
        private static readonly UID128 Tool_Workload = new UID128("<randomly minted uid>");

        private static readonly UID128 Agent_Impact     = new UID128("<randomly minted uid>");
        private static readonly UID128 Agent_Comparison = new UID128("<randomly minted uid>");

        private static readonly UID128[] PinnedTools  = { Tool_Search, Tool_Consult, Tool_Workload, Tool_Sandbox };
        private static readonly UID128[] PinnedAgents = { Agent_Impact, Agent_Comparison };

        /// <summary>
        /// The data-parsing pipeline that CARRIES THE SPOTTERS — the one config/pipeline-mapping.json
        /// maps every parsed field to (check its Models list in nlp/pipelines/). It is exactly the
        /// vocabulary an assistant reply is written in. The language-neutral pipeline, or one
        /// without the spotters, parses replies and links nothing.
        /// </summary>
        private static readonly UID128 DataParsingPipeline = new UID128("<pipeline uid>");

        /// <summary>
        /// Opens on what is answerable FROM AN EMPTY CHAT. Every one of these is a workload or
        /// a corpus question the assistant settles on its own — the judgement questions the
        /// agents exist for need records in the conversation, and those arrive through a
        /// record's "Ask" button (see AiAssist), not through a suggestion pill.
        /// </summary>
        private static readonly string[] WelcomePrompts =
        {
            "What is on my desk right now?".t(),
            "Which of my cases are urgent?".t(),
            "Is there anything unassigned I could pick up?".t(),
            "Has anyone already looked at the forward bay wiring?".t(),
            // A sandbox question: counting across many records is what it is for.
            "How many open cases are there per category, and which category is oldest?".t(),
        };

        /// <summary>
        /// Built ONCE and shared by every visit. The configuration holds no per-chat state —
        /// ChatView takes all of that from the Parameters it is constructed with — so
        /// rebuilding it per navigation would only cost allocations.
        /// </summary>
        private static readonly ChatViewConfiguration Configuration = new ChatViewConfiguration
        {
            Title                  = "Analysis Assistant".t(),
            WelcomeTitle           = "What are we analysing?".t(),
            WelcomePrompts         = WelcomePrompts,
            ChatBoxPlaceholderText = "Ask about a change, a case, or what is on your desk".t(),

            // Neither is the user's to change, so neither gets a picker. Dropping the tool
            // picker is what makes the pinned set below the only answer to "what can it use" —
            // a picker left in place with nothing in it is a control that can only disappoint.
            DisableAssistantSelector = true,
            DisableTools             = true,

            // The sources behind an answer ARE the answer here: a verdict on a candidate is
            // only worth reading next to the records it was reached from.
            ShowCitationsPanel       = true,

            // An answer here is written in identifiers. Parsing every reply against the corpus
            // turns each one into the record it names, opening in the preview pane beside the
            // conversation. A mention the parse cannot resolve, or one the reader has no access
            // to, stays plain text.
            ParseAs = new ChatViewParseAs
            {
                PipelineUID = DataParsingPipeline,
                NodeType    = N.Change.Type,
                FieldName   = N.Change.Title,
            },

            ChatRoute      = chatUID => DefaultRoutes.ChatAI.OpenChat(chatUID, default),

            NewChat        = NewChatAsync,
            PostMessage    = PostMessageAsync,
            ReplaceMessage = ReplaceMessageAsync,
        };

        // It deliberately does NOT set a Category: the chats AiAssist opens from a record's
        // "Ask" button are uncategorised, and a categorised view would list its own chats and
        // not those — the button and this page have to be two ways into one history.

        public static IComponent Create(Parameters state) => new ChatView(Configuration, state);

        /// <summary>
        /// Starts every chat on this application's assistant, whatever the workspace's selected
        /// template happens to be. The PROVIDER is still the user's — which model answers is a
        /// preference; which assistant answers is what this page IS.
        /// </summary>
        private static Task<ChatMetadata> NewChatAsync(ChatViewConfiguration.NewChatRequest request)
            => Mosaik.API.ChatAI.NewChat(
                request.ProviderUID ?? App.InterfaceSettings.ChatAIProvider?.TaskUID,
                UID128.Parse(AiAssist.AssistantUID),
                request.Category);

        /// <summary>
        /// Posts with the PINNED tools and agents rather than with the composer's selection.
        /// There is no selection to pass on: DisableTools leaves the view without a picker, so
        /// request.ActiveTools always arrives empty and passing it through would run the turn
        /// with no capabilities at all.
        ///
        /// <para>Skills are passed through untouched: null means "not chosen", which is what
        /// lets the run fall back to the assistant's own skills.</para>
        /// </summary>
        private static Task<UID128> PostMessageAsync(ChatViewConfiguration.PostMessageRequest request)
            => Mosaik.API.ChatAI.PostMessage(
                chatUID:           request.ActiveChat.UID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         PinnedAgents,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);

        /// <summary>
        /// The SAME pinning for an edited or regenerated message. Overriding PostMessage alone
        /// would leave "Edit" and "Regenerate" re-running the turn without tools, which reads
        /// as the assistant having got worse.
        /// </summary>
        private static Task<UID128> ReplaceMessageAsync(ChatViewConfiguration.ReplaceMessageRequest request)
            => Mosaik.API.ChatAI.ReplaceMessage(
                chatUID:           request.ActiveChat.UID,
                messageUID:        request.MessageUID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         PinnedAgents,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);
    }
}
