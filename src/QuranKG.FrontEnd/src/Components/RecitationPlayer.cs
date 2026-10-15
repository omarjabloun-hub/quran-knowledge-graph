using System;
using System.Linq;
using QuranKG.FrontEnd.Api;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// A surah recited, as a bar under the text: play and pause, the verse before and after, repeat
    /// a verse, the speed, and whether the text follows. While it plays, it tells the reader which
    /// verse is being recited (<see cref="Settings.OnVerse"/>) — the reader marks it and keeps it in
    /// view.
    ///
    /// <para>The recording is streamed from where its source hosts it; where each verse starts in it
    /// comes from qkg/surah/recitation. A recitation in another reading counts its verses its own
    /// way (Qalun: 285 verses in Al-Baqarah): the times are already the corpus's verses, a few of
    /// them estimated (<see cref="QK.RecitedVerseRow.Exact"/>), which the bar says.</para>
    /// </summary>
    internal sealed class RecitationPlayer : IComponent
    {
        internal sealed class Settings
        {
            internal UID128 Surah    { get; set; }
            internal string VerseKeyPrefix { get; set; }
            /// <summary>The verse being recited; null when the recitation stops or closes.</summary>
            internal Action<QK.RecitedVerseRow> OnVerse { get; set; }
        }

        private static readonly double[] Speeds = { 0.75, 1, 1.25, 1.5 };
        private const string FOLLOW_KEY = "qkg-player-follow";
        private const string SPEED_KEY  = "qkg-player-speed";

        private readonly Settings         _settings;
        private readonly HTMLAudioElement _audio;
        private readonly Stack            _root;
        private readonly Stack            _body;
        private readonly HTMLElement      _progress;
        private readonly HTMLElement      _progressFill;

        private QK.RecitationDto _recitation;
        private HTMLElement      _play, _speed, _repeat, _follow;
        private TextBlock        _where, _time;
        private int              _current = -1;
        private int              _generation;
        private bool             _playing;
        private bool             _repeatVerse;
        private int              _speedIndex = 1;
        private int              _pendingAyah = -1;

        internal bool Follow { get; private set; } = true;
        internal bool IsOpen { get; private set; }

        public RecitationPlayer(Settings settings)
        {
            _settings = settings;
            Follow    = Load(FOLLOW_KEY) != "0";
            if (int.TryParse(Load(SPEED_KEY), out var speed) && speed >= 0 && speed < Speeds.Length) _speedIndex = speed;

            _audio = document.createElement("audio").As<HTMLAudioElement>();
            _audio.preload = "metadata";
            _audio.ontimeupdate     = _ => Tick();
            _audio.onended          = _ => { SetPlaying(false); Mark(-1); };
            _audio.onloadedmetadata = _ =>
            {
                _audio.playbackRate = Speeds[_speedIndex];
                if (_pendingAyah >= 0) { var a = _pendingAyah; _pendingAyah = -1; Seek(a); }
            };

            _progress     = Div(Att("qkg-player-track"));
            _progressFill = Div(Att("qkg-player-fill"));
            _progress.appendChild(_progressFill);
            _progress.onclick = e =>
            {
                if (_recitation is null || _recitation.Duration <= 0) return;
                var rect  = _progress.getBoundingClientRect().As<DOMRect>();
                var share = Math.Max(0, Math.Min(1, (e.As<MouseEvent>().clientX - rect.left) / rect.width));
                _audio.currentTime = share * _recitation.Duration;
                Tick();
            };

            _body = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap();
            EnsureStyles();
            _root = VStack().WS().Class("qkg-player").Children(Raw(_progress).WS(), _body.PL(32).PR(32).PT(10).PB(12));
            _root.Collapse();
        }

        public HTMLElement Render() => _root.Render();

        /// <summary>Shows the bar and starts the recitation at the verse given (0: the surah's start, Bismillah included).</summary>
        internal void PlayFrom(int ayah)
        {
            if (!IsOpen) Open();
            if (_recitation is null) { _pendingAyah = Math.Max(ayah, 0); _autoplay = true; return; }
            Seek(ayah);
            Play();
        }

        private bool _autoplay;

        internal void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _root.Show();
            if (_recitation is object) return;

            _body.Clear();
            _body.Add(TextBlock("Loading the recitation…".t()).Small().Secondary());
            Load().FireAndForget();
        }

        internal void Close()
        {
            _audio.pause();
            SetPlaying(false);
            Mark(-1);
            IsOpen = false;
            _root.Collapse();
        }

        // -----------------------------------------------------------------------------------

        private async System.Threading.Tasks.Task Load()
        {
            var dto = await QuranApi.SurahRecitationAsync(_settings.Surah);
            _recitation = (dto?.Recitations ?? new QK.RecitationDto[0]).FirstOrDefault();

            _body.Clear();
            if (_recitation is null)
            {
                _body.Add(TextBlock("No recitation of this surah in the graph yet.".t()).Small().Secondary().Grow());
                _body.Add(Button().SetIcon(UIcons.Cross).Compact().Tooltip("Close".t()).OnClick(Close));
                return;
            }

            _play = Look.IconButton("", "Play / pause".t(), Toggle);
            _play.classList.add("qkg-on");
            _play.classList.add("qkg-player-play");
            _play.appendChild(Icon(UIcons.PlayAlt).Render());
            var previous = Look.IconButton("‹", "The verse before".t(), () => Step(-1));
            var next     = Look.IconButton("›", "The verse after".t(), () => Step(+1));

            _repeat = Look.IconButton("", "Repeat the verse being recited".t(), null);
            _repeat.appendChild(Icon(UIcons.ArrowsRepeat).Render());
            _repeat.setAttribute("aria-pressed", "false");
            _repeat.onclick = _ =>
            {
                _repeatVerse = !_repeatVerse;
                if (_repeatVerse) _repeat.classList.add("qkg-on"); else _repeat.classList.remove("qkg-on");
                _repeat.setAttribute("aria-pressed", _repeatVerse ? "true" : "false");
            };

            _follow = Look.Chip("Follow".t(), null, Follow);
            _follow.title = "Keep the verse being recited in view".t();
            _follow.setAttribute("aria-pressed", Follow ? "true" : "false");
            _follow.onclick = _ =>
            {
                Follow = !Follow;
                if (Follow) _follow.classList.add("qkg-on"); else _follow.classList.remove("qkg-on");
                _follow.setAttribute("aria-pressed", Follow ? "true" : "false");
                Save(FOLLOW_KEY, Follow ? "1" : "0");
            };

            _speed = Look.Button(SpeedLabel(), null, "ghost", null, "Speed".t());
            _speed.onclick = _ =>
            {
                _speedIndex = (_speedIndex + 1) % Speeds.Length;
                _audio.playbackRate = Speeds[_speedIndex];
                _speed.querySelector("span").As<HTMLElement>().textContent = SpeedLabel();
                Save(SPEED_KEY, _speedIndex.ToString());
            };

            _where = TextBlock("").SemiBold().Small();
            _time  = TextBlock("").Tiny().Secondary();

            var reciter = HStack().AlignItemsCenter().Gap(10.px()).Children(
                Arabic.Inline(_recitation.ReciterNameArabic, 18),
                VStack().Children(
                    TextBlock(_recitation.ReciterName).Small(),
                    TextBlock(_recitation.Riwaya + " · " + _recitation.Country).Tiny().Secondary()))
               .Tooltip(About());

            var transport = Look.El("div", "qkg-player-transport", previous, _play, next);
            _body.Add(Raw(transport));
            _body.Add(VStack().Children(_where, _time).W(150.px()));
            _body.Add(reciter.Grow());
            _body.Add(Raw(Look.El("div", "qkg-player-tools", _speed, _repeat, _follow, Look.IconButton("✕", "Stop and close".t(), Close))));

            _audio.src = _recitation.AudioUrl;
            Show(-1);

            if (_autoplay)
            {
                _autoplay = false;
                Play();
            }
        }

        private string About()
            => string.Format("Sheikh {0} recites in the reading of {1} ({2}). The text shown is Hafs's, so a few words are read differently, and {1} counts the verses its own way: {3} in this surah, {4} in the text. A verse that starts inside one of the recording's verses is marked ≈ — its start is estimated. {5}".t(),
                             _recitation.ReciterName, _recitation.Riwaya, _recitation.RiwayaArabic, _recitation.RecordedVerses, _recitation.Verses?.Length ?? 0, _recitation.Attribution);

        private string SpeedLabel() => Speeds[_speedIndex].ToString("0.##") + "×";

        private void Toggle()
        {
            if (_recitation is null) return;
            if (_playing) { _audio.pause(); SetPlaying(false); }
            else Play();
        }

        private void Play()
        {
            _audio.playbackRate = Speeds[_speedIndex];
            _audio.play();
            SetPlaying(true);
        }

        private void SetPlaying(bool playing)
        {
            _playing = playing;
            if (_play is object)
            {
                _play.innerHTML = "";
                _play.appendChild(Icon(playing ? UIcons.Pause : UIcons.PlayAlt).Render());
            }
            if (playing)
            {
                var generation = ++_generation;
                Loop(generation);
            }
        }

        /// <summary>Ten times a second while playing: the timeupdate event alone comes every 250 ms, late for a short verse.</summary>
        private void Loop(int generation)
        {
            if (generation != _generation || !_playing) return;
            Tick();
            window.setTimeout(_ => Loop(generation), 100);
        }

        private void Step(int delta)
        {
            var verses = _recitation?.Verses ?? new QK.RecitedVerseRow[0];
            if (verses.Length == 0) return;
            var target = Math.Max(0, Math.Min(verses.Length - 1, (_current < 0 ? 0 : _current) + delta));
            Seek(verses[target].Ayah);
            if (!_playing) Play();
        }

        /// <summary>To the start of a verse; 0 is the start of the recording (the Bismillah, recited before verse 1).</summary>
        private void Seek(int ayah)
        {
            var verses = _recitation?.Verses ?? new QK.RecitedVerseRow[0];
            var row    = verses.FirstOrDefault(v => v.Ayah == ayah);
            _audio.currentTime = ayah <= 1 || row is null ? 0 : row.Start;
            Tick();
        }

        private void Tick()
        {
            if (_recitation is null) return;
            var t      = _audio.currentTime;
            var verses = _recitation.Verses ?? new QK.RecitedVerseRow[0];

            var index = -1;
            for (var i = 0; i < verses.Length; i++)
            {
                if (verses[i].Start <= t) index = i;
                else break;
            }

            if (_repeatVerse && _playing && _current >= 0 && _current < verses.Length && t >= verses[_current].End - 0.05)
            {
                _audio.currentTime = verses[_current].Start;
                return;
            }

            if (index != _current) Mark(index);
            Show(index);

            var duration = _recitation.Duration > 0 ? _recitation.Duration : 1;
            _progressFill.style.width = (100 * Math.Min(1, t / duration)).ToString("0.###") + "%";
        }

        private void Mark(int index)
        {
            _current = index;
            var verses = _recitation?.Verses ?? new QK.RecitedVerseRow[0];
            _settings.OnVerse?.Invoke(index >= 0 && index < verses.Length ? verses[index] : null);
        }

        private void Show(int index)
        {
            if (_where is null) return;
            var verses = _recitation.Verses ?? new QK.RecitedVerseRow[0];
            if (index >= 0 && index < verses.Length)
            {
                var v = verses[index];
                _where.Text = (v.Exact ? "" : "≈ ") + string.Format("Verse {0}{1}".t(), _settings.VerseKeyPrefix ?? "", v.Ayah);
                _where.Tooltip(v.Exact ? "" : "This verse starts inside one of the recording's verses (its reading counts the verses another way): its start is estimated from the letters before it.".t());
            }
            else _where.Text = _audio.currentTime > 0 ? "Bismillah".t() : "";

            _time.Text = Clock(_audio.currentTime) + " / " + Clock(_recitation.Duration);
        }

        private static string Clock(double seconds)
        {
            var s = (int)Math.Floor(Math.Max(0, seconds));
            return s >= 3600 ? string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60) : string.Format("{0}:{1:00}", s / 60, s % 60);
        }

        private static string Load(string key)
        {
            try { return localStorage.getItem(key) ?? ""; } catch { return ""; }
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            Look.EnsureStyles();
            var style = document.createElement("style");
            style.textContent = @"
.qkg-player { background: var(--qkg-raised); border-top: 1px solid var(--qkg-line); color: var(--qkg-ink); }
.qkg-player-track { height: 4px; background: var(--qkg-surface-2); cursor: pointer; }
.qkg-player-track:hover { height: 6px; }
.qkg-player-fill { height: 100%; width: 0; background: var(--qkg-teal); }
.qkg-player-transport, .qkg-player-tools { display: inline-flex; align-items: center; gap: 6px; }
.qkg-player-play { width: 44px; height: 44px; font-size: 16px; }
";
            document.head.appendChild(style);
        }

        private static void Save(string key, string value)
        {
            try { localStorage.setItem(key, value); } catch { /* nothing to keep it in */ }
        }
    }
}
