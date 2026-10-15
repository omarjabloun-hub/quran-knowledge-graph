using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Components;
using Tesserae;
using TNT;
using UID;
using static TNT.T;

namespace QuranKG.FrontEnd.Chat
{
    /// <summary>
    /// The chat page: the Quran Research Assistant with its tools, on every turn.
    ///
    /// <para>The stock chat lets the reader pick an assistant and, per chat, which tools it may use —
    /// and starts each chat with only consult ticked, so the assistant could read a verse but not
    /// count a word, and answered "how many times" from memory. Here the assistant and its tools are
    /// pinned and both pickers are hidden: the method in its prompt needs all of them.</para>
    ///
    /// <para>The verses a message names are links (<see cref="VerseReferences"/>): a click reads the
    /// verse in its surah without leaving the chat.</para>
    ///
    /// <para>The UIDs restate the header of code/ai-assistants/quran-research-assistant-defaULtAiASSistaNt1111.cs,
    /// so an edit to the assistant in the admin UI cannot quietly take a tool away from the page.</para>
    /// </summary>
    internal static class QuranChatView
    {
        /// <summary>The workspace's default assistant, which this application defines (code/ai-assistants).</summary>
        private const string AssistantUID = "defaULtAiASSistaNt1111";

        /// <summary>CountTerm, FindVerses, ReadVerses, SameCount, SimilarVerses, SimilarSurahs.</summary>
        private static readonly UID128 Tool_Quran   = new UID128("QuranTooLs111111111111");
        /// <summary>The sandbox: the corpus as files, with bash and python.</summary>
        private static readonly UID128 Tool_Sandbox = new UID128("QuranFiLes111111111111");
        /// <summary>The workspace search, fenced to the types the assistant may read.</summary>
        private static readonly UID128 Tool_Search  = new UID128("SEARCHRecords111111111");
        /// <summary>The readers of verses, words, roots, lemmas, surahs, passages, themes and hadith.</summary>
        private static readonly UID128 Tool_Consult = new UID128("ConsuLt111111111111111");
        /// <summary>FindHadith, ReadHadith, HadithForVerse: Bukhari and Muslim against the Quran.</summary>
        private static readonly UID128 Tool_Hadith  = new UID128("HadiThTooLs11111111111");

        private static readonly UID128[] PinnedTools = { Tool_Quran, Tool_Hadith, Tool_Sandbox, Tool_Search, Tool_Consult };

        /// <summary>Questions an empty chat can answer, one per kind of tool.</summary>
        private static readonly string[] WelcomePrompts =
        {
            "Is Jesus mentioned as many times as Adam? Check it under each counting rule.".t(),
            "In which verses do the roots رحم and غفر occur together?".t(),
            "Read 2:255 word by word, with each word's root and lemma.".t(),
            "Which surah is the most similar to Al-Baqarah, and why?".t(),
            "Among the prophets' names, which pairs occur equally often — and how often would that happen by chance?".t(),
            "Is Bukhari 6018 backed by the Quran? Read it against the verses closest to it.".t(),
        };

        private static readonly ChatViewConfiguration Configuration = new ChatViewConfiguration
        {
            Title                    = "Quran Research Assistant".t(),
            WelcomeTitle             = "What shall we look into in the text?".t(),
            WelcomePrompts           = WelcomePrompts,
            ChatBoxPlaceholderText   = "Ask about a word, a verse, a count or a pattern".t(),

            DisableAssistantSelector = true,
            DisableTools             = true,
            ShowCitationsPanel       = true,

            ChatRoute                = chatUID => DefaultRoutes.ChatAI.OpenChat(chatUID, default),

            // "21:30" in a question or an answer opens the verse in its surah, over the chat.
            CustomMessageRenderer    = (chat, message, content) => VerseReferences.Linkify(content),

            NewChat                  = NewChatAsync,
            PostMessage              = PostMessageAsync,
            ReplaceMessage           = ReplaceMessageAsync,
        };

        public static IComponent Create(Parameters state) => new ChatView(Configuration, state);

        /// <summary>Every chat on this assistant; the provider (which model answers) stays the reader's choice.</summary>
        private static Task<ChatMetadata> NewChatAsync(ChatViewConfiguration.NewChatRequest request)
            => Mosaik.API.ChatAI.NewChat(
                request.ProviderUID ?? App.InterfaceSettings.ChatAIProvider?.TaskUID,
                UID128.Parse(AssistantUID),
                request.Category);

        /// <summary>The pinned tools, not the composer's selection: with the picker hidden that is always empty.</summary>
        private static Task<UID128> PostMessageAsync(ChatViewConfiguration.PostMessageRequest request)
            => Mosaik.API.ChatAI.PostMessage(
                chatUID:           request.ActiveChat.UID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         null,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);

        /// <summary>The same pinning for an edited or regenerated message.</summary>
        private static Task<UID128> ReplaceMessageAsync(ChatViewConfiguration.ReplaceMessageRequest request)
            => Mosaik.API.ChatAI.ReplaceMessage(
                chatUID:           request.ActiveChat.UID,
                messageUID:        request.MessageUID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         null,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);
    }
}
