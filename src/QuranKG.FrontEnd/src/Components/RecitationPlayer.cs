using System;
using System.Collections.Generic;
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
    /// The recitation, one for the whole app: a bar docked at the foot of the window that stays
    /// while the reader goes from page to page — the surah being recited is its title (its Arabic
    /// name, a click back to the verse being recited), then the verse and the time; play and pause,
    /// the verse before and after, repeat a verse, the speed, and whether the text follows.
    ///
    /// <para>A reader showing the surah being recited watches it (<see cref="Watch"/>): it marks the
    /// verse being recited and keeps it in view — again when the reader comes back to the surah.
    /// A reader gone from the page is forgotten.</para>
    ///
    /// <para>The recording is streamed from where its source hosts it; where each verse starts in it
    /// comes from qkg/surah/recitation. Read in the recording's own reading (Qalun's text, Sheikh
    /// Latrech's Qalun), the text follows the recording's own verses, every one timed exactly
    /// (<see cref="QK.RecitationDto.RecordedVerseRows"/>). Read in another (Hafs), whose verses the
    /// recording counts its own way, the times are carried over to the text's verses, a few of them
    /// estimated (<see cref="QK.RecitedVerseRow.Exact"/>), which the bar says.</para>
    /// </summary>
    internal sealed class RecitationPlayer
    {
        /// <summary>A surah to recite, as a reader shows it: in which reading its verses are numbered.</summary>
        internal sealed class Track
        {
            internal UID128 Surah           { get; set; }
            internal int    SurahNumber     { get; set; }
            internal string SurahName       { get; set; }
            internal string SurahNameArabic { get; set; }
            /// <summary>"qalun", "hafs": the numbering of the ayahs asked for.</summary>
            internal string Riwaya          { get; set; }
        }

        private sealed class Watcher
        {
            internal HTMLElement                Owner;
            internal UID128                     Surah;
            internal string                     Riwaya;
            internal Action<QK.RecitedVerseRow> OnVerse;
            /// <summary>Seen on the page: once gone from it, the reader is forgotten.</summary>
            internal bool                       Seen;
        }

        private static readonly double[] Speeds = { 0.75, 1, 1.25, 1.5 };
        private const string FOLLOW_KEY = "qkg-player-follow";
        private const string SPEED_KEY  = "qkg-player-speed";

        private static RecitationPlayer _instance;
        /// <summary>The one player; its bar is added to the page the first time it is needed.</summary>
        internal static RecitationPlayer Instance => _instance ?? (_instance = new RecitationPlayer());

        private readonly List<Watcher>    _watchers = new List<Watcher>();
        private readonly HTMLAudioElement _audio;
        private readonly HTMLElement      _root;
        private readonly HTMLElement      _body;
        private readonly HTMLElement      _progressFill;

        private Track                _track;
        private QK.RecitationDto     _recitation;
        private string               _rowsRiwaya;
        /// <summary>The verses timed, in the numbering of the reading followed: the recording's own when the text is in its reading.</summary>
        private QK.RecitedVerseRow[] _rows = new QK.RecitedVerseRow[0];
        private bool                 _ownReading;
        private HTMLElement          _play, _speed, _repeat, _follow, _where, _time, _reciter;
        private int                  _current = -1;
        private int                  _generation;
        private int                  _loading;
        private bool                 _playing;
        private bool                 _repeatVerse;
        private int                  _speedIndex = 1;

        internal bool Follow { get; private set; } = true;
        internal bool IsOpen { get; private set; }

        private RecitationPlayer()
        {
            EnsureStyles();
            Arabic.EnsureFonts();
            Follow = Load(FOLLOW_KEY) != "0";
            if (int.TryParse(Load(SPEED_KEY), out var speed) && speed >= 0 && speed < Speeds.Length) _speedIndex = speed;

            _audio = document.createElement("audio").As<HTMLAudioElement>();
            _audio.preload = "metadata";
            _audio.ontimeupdate     = _ => Tick();
            _audio.onended          = _ => { SetPlaying(false); Mark(-1); };
            _audio.onloadedmetadata = _ => _audio.playbackRate = Speeds[_speedIndex];

            var track = Look.El("div", "qkg-player-track");
            _progressFill = Look.El("div", "qkg-player-fill");
            track.appendChild(_progressFill);
            track.onclick = e =>
            {
                if (_recitation is null || _recitation.Duration <= 0) return;
                var rect  = track.getBoundingClientRect().As<DOMRect>();
                var share = Math.Max(0, Math.Min(1, (e.As<MouseEvent>().clientX - rect.left) / rect.width));
                _audio.currentTime = share * _recitation.Duration;
                Tick();
            };

            _body = Look.El("div", "qkg-player-body");
            _root = Look.El("div", "qkg-ui qkg-player-dock", track, _body);
            _root.style.display = "none";
            _root.onmouseenter = _ => Dock();
            window.addEventListener("resize", (Action<Event>)(_ => Dock()));
            document.body.appendChild(_root);
        }

        // ---- what readers call -------------------------------------------------------------------

        /// <summary>Recites <paramref name="track"/> from a verse (in its reading's numbering; 0: the surah's start, Bismillah included).</summary>
        internal static void Play(Track track, int ayah) => Instance.PlayTrack(track, ayah);

        /// <summary>
        /// A reader showing a surah: told the verse being recited while that surah plays, until the
        /// reader leaves the page (<paramref name="owner"/> no longer in it). Reading the surah being
        /// recited in another reading makes the player follow that reading's verses.
        /// </summary>
        internal static void Watch(HTMLElement owner, UID128 surah, string riwaya, Action<QK.RecitedVerseRow> onVerse)
        {
            var p = Instance;
            p._watchers.RemoveAll(w => w.Owner == owner || (w.Seen && !w.Owner.isConnected));
            p._watchers.Add(new Watcher { Owner = owner, Surah = surah, Riwaya = riwaya, OnVerse = onVerse });

            if (p.IsOpen && p._track is object && p._track.Surah == surah)
            {
                if (riwaya != p._rowsRiwaya) p.UseReading(riwaya);
                var row = p._current >= 0 && p._current < p._rows.Length ? p._rows[p._current] : null;
                if (row is object) window.setTimeout(_ => onVerse(row), 200);
            }
        }

        internal void Close()
        {
            _audio.pause();
            SetPlaying(false);
            Mark(-1);
            IsOpen = false;
            _root.style.display = "none";
            document.body.classList.remove("qkg-player-on");
        }

        // -----------------------------------------------------------------------------------------

        private void PlayTrack(Track track, int ayah)
        {
            Open();
            if (_track is object && _recitation is object && _track.Surah == track.Surah)
            {
                _track = track;
                if (track.Riwaya != _rowsRiwaya) UseReading(track.Riwaya);
                Seek(ayah);
                Play();
                return;
            }

            _audio.pause();
            SetPlaying(false);
            Mark(-1);
            _track      = track;
            _recitation = null;
            _rows       = new QK.RecitedVerseRow[0];
            DrawLoading();
            LoadAndPlay(track, ayah, ++_loading).FireAndForget();
        }

        private void Open()
        {
            Dock();
            if (IsOpen) return;
            IsOpen = true;
            _root.style.display = "";
            document.body.classList.add("qkg-player-on");
        }

        private async System.Threading.Tasks.Task LoadAndPlay(Track track, int ayah, int loading)
        {
            var dto = await QuranApi.SurahRecitationAsync(track.Surah);
            if (loading != _loading) return;   // another surah asked for meanwhile

            _recitation = (dto?.Recitations ?? new QK.RecitationDto[0]).FirstOrDefault();
            if (_recitation is null) { DrawMissing(); return; }

            UseReading(track.Riwaya);
            Draw();
            _audio.src = _recitation.AudioUrl;
            _audio.playbackRate = Speeds[_speedIndex];

            // Seek once the recording can be: before its metadata, currentTime is not kept.
            void Start()
            {
                Seek(ayah);
                Play();
            }
            if (_audio.readyState >= 1) Start();
            else
            {
                Action<Event> once = null;
                once = _ => { _audio.removeEventListener("loadedmetadata", once); Start(); };
                _audio.addEventListener("loadedmetadata", once);
            }
        }

        /// <summary>The verses followed: the recording's own when the reading is its own, else carried over to the reading's.</summary>
        private void UseReading(string riwaya)
        {
            _rowsRiwaya = riwaya;
            if (_recitation is null) return;
            _ownReading = !string.IsNullOrEmpty(_recitation.RiwayaId) && _recitation.RiwayaId == riwaya
                       && (_recitation.RecordedVerseRows ?? new QK.RecitedVerseRow[0]).Length > 0;
            _rows    = (_ownReading ? _recitation.RecordedVerseRows : _recitation.Verses) ?? new QK.RecitedVerseRow[0];
            _current = -2;   // forces the next tick to tell the readers
            if (_reciter is object) _reciter.title = About();
            Tick();
        }

        // ---- the bar ------------------------------------------------------------------------------

        private void DrawLoading()
        {
            _body.innerHTML = "";
            _body.appendChild(Title());
            _body.appendChild(Look.El("span", "qkg-note", "Loading the recitation…".t()));
            _body.appendChild(Look.El("span", "qkg-toolbar-spacer"));
            _body.appendChild(Look.IconButton("✕", "Close".t(), Close));
        }

        private void DrawMissing()
        {
            _body.innerHTML = "";
            _body.appendChild(Title());
            _body.appendChild(Look.El("span", "qkg-note", "No recitation of this surah in the graph yet.".t()));
            _body.appendChild(Look.El("span", "qkg-toolbar-spacer"));
            _body.appendChild(Look.IconButton("✕", "Close".t(), Close));
        }

        /// <summary>The surah: its Arabic name first, then its number and name, then the verse and the time.</summary>
        private HTMLElement Title()
        {
            var title = Look.El("div", "qkg-player-title");
            var name  = Look.El("div", "qkg-player-name");
            name.appendChild(Look.Ar(_track?.SurahNameArabic ?? "", "qkg-arw qkg-player-name-ar"));
            name.appendChild(Look.El("span", "qkg-player-name-en", (_track?.SurahNumber ?? 0) + " · " + (_track?.SurahName ?? "")));
            name.title = "Back to the verse being recited".t();
            title.appendChild(Look.OnClick(name, GoToVerse));

            _where = Look.El("span", "qkg-player-where", "");
            _time  = Look.El("span", "qkg-player-time", "");
            title.appendChild(Look.El("div", "qkg-player-sub", _where, _time));
            return title;
        }

        private void Draw()
        {
            _body.innerHTML = "";

            _play = Look.IconButton("", "Play / pause".t(), Toggle);
            _play.classList.add("qkg-on");
            _play.classList.add("qkg-player-play");
            _play.appendChild(Icon(UIcons.PlayAlt).Render());
            var previous = Look.IconButton("‹", "The verse before".t(), () => Step(-1));
            var next     = Look.IconButton("›", "The verse after".t(), () => Step(+1));

            _repeat = Look.IconButton("", "Repeat the verse being recited".t(), null);
            _repeat.appendChild(Icon(UIcons.ArrowsRepeat).Render());
            _repeat.setAttribute("aria-pressed", _repeatVerse ? "true" : "false");
            if (_repeatVerse) _repeat.classList.add("qkg-on");
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

            _reciter = Look.El("div", "qkg-player-reciter",
                Look.Ar(_recitation.ReciterNameArabic, "qkg-arw qkg-player-reciter-ar"),
                Look.El("span", "qkg-player-reciter-en", _recitation.ReciterName + " · " + _recitation.Riwaya));
            _reciter.title = About();

            _body.appendChild(Title());
            _body.appendChild(Look.El("div", "qkg-player-transport", previous, _play, next));
            _body.appendChild(_reciter);
            _body.appendChild(Look.El("div", "qkg-player-tools", _speed, _repeat, _follow, Look.IconButton("✕", "Stop and close".t(), Close)));
            Show(_current);
        }

        private string About()
            => _ownReading
               ? string.Format("Sheikh {0} recites in the reading of {1} ({2}), the reading of the text: each of its {3} verses here is followed where the recording itself marks it. {4}".t(),
                               _recitation.ReciterName, _recitation.Riwaya, _recitation.RiwayaArabic, _recitation.RecordedVerses, _recitation.Attribution)
               : string.Format("Sheikh {0} recites in the reading of {1} ({2}). The text shown is in another reading, so a few words are read differently, and {1} counts the verses its own way: {3} in this surah, {4} in the text. A verse that starts inside one of the recording's verses is marked ≈ — its start is estimated. {5}".t(),
                               _recitation.ReciterName, _recitation.Riwaya, _recitation.RiwayaArabic, _recitation.RecordedVerses, _recitation.Verses?.Length ?? 0, _recitation.Attribution);

        private string SpeedLabel() => Speeds[_speedIndex].ToString("0.##") + "×";

        /// <summary>The surah's name, clicked: the Quran View at the verse being recited (its Hafs verse, as every link).</summary>
        private void GoToVerse()
        {
            if (_track is null) return;
            var row = _current >= 0 && _current < _rows.Length ? _rows[_current] : null;
            QuranLinks.Go(_track.SurahNumber, row?.HafsAyah ?? 0);
        }

        /// <summary>The bar beside the sidebar, whatever its width.</summary>
        private void Dock()
        {
            var sidebar = document.querySelector(".tss-sidebar");
            var left    = sidebar is null ? 0 : sidebar.As<HTMLElement>().getBoundingClientRect().As<DOMRect>().right;
            _root.style.setProperty("--qkg-dock-left", Math.Round(left) + "px");
        }

        // ---- playing --------------------------------------------------------------------------------

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
            if (_rows.Length == 0) return;
            var target = Math.Max(0, Math.Min(_rows.Length - 1, (_current < 0 ? 0 : _current) + delta));
            Seek(_rows[target].Ayah);
            if (!_playing) Play();
        }

        /// <summary>To the start of a verse; 0 or 1 is the start of the recording (the Bismillah, recited before verse 1).</summary>
        private void Seek(int ayah)
        {
            var row = _rows.FirstOrDefault(v => v.Ayah == ayah);
            _audio.currentTime = ayah <= 1 || row is null ? 0 : row.Start;
            Tick();
        }

        private void Tick()
        {
            if (_recitation is null) return;
            var t = _audio.currentTime;

            var index = -1;
            for (var i = 0; i < _rows.Length; i++)
            {
                if (_rows[i].Start <= t) index = i;
                else break;
            }

            if (_repeatVerse && _playing && _current >= 0 && _current < _rows.Length && t >= _rows[_current].End - 0.05)
            {
                _audio.currentTime = _rows[_current].Start;
                return;
            }

            if (index != _current) Mark(index);
            Show(index);

            var duration = _recitation.Duration > 0 ? _recitation.Duration : 1;
            _progressFill.style.width = (100 * Math.Min(1, t / duration)).ToString("0.###") + "%";
        }

        /// <summary>The verse being recited, told to every reader of the surah still on the page.</summary>
        private void Mark(int index)
        {
            _current = index;
            var row = index >= 0 && index < _rows.Length ? _rows[index] : null;
            foreach (var w in _watchers) if (w.Owner.isConnected) w.Seen = true;
            _watchers.RemoveAll(w => w.Seen && !w.Owner.isConnected);
            foreach (var w in _watchers.ToArray())
                if (_track is object && w.Surah == _track.Surah && w.Owner.isConnected && w.Riwaya == _rowsRiwaya) w.OnVerse(row);
        }

        private void Show(int index)
        {
            if (_where is null || _recitation is null) return;
            if (index >= 0 && index < _rows.Length)
            {
                var v = _rows[index];
                _where.textContent = (v.Exact ? "" : "≈ ") + string.Format("Verse {0}:{1}".t(), _track?.SurahNumber ?? 0, v.Ayah);
                _where.title = v.Exact ? "" : "This verse starts inside one of the recording's verses (its reading counts the verses another way): its start is estimated from the letters before it.".t();
            }
            else _where.textContent = _audio.currentTime > 0 ? "Bismillah".t() : "";

            _time.textContent = Clock(_audio.currentTime) + " / " + Clock(_recitation.Duration);
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

        private static void Save(string key, string value)
        {
            try { localStorage.setItem(key, value); } catch { /* nothing to keep it in */ }
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            Look.EnsureStyles();
            var style = document.createElement("style");
            // Docked over the page, beside the sidebar; the pages leave room for it while it is open.
            style.textContent = @"
.qkg-player-dock { position: fixed; z-index: 900; bottom: 14px; left: calc(var(--qkg-dock-left, 0px) + 16px); right: 16px; max-width: 1080px; margin: 0 auto;
                   background: var(--qkg-raised); color: var(--qkg-ink); border: 1px solid var(--qkg-line); border-radius: 18px; overflow: hidden;
                   box-shadow: 0 10px 30px rgba(15, 23, 42, .16), 0 2px 6px rgba(15, 23, 42, .08); }
.qkg-player-track { height: 4px; background: var(--qkg-surface-2); cursor: pointer; }
.qkg-player-track:hover { height: 6px; }
.qkg-player-fill { height: 100%; width: 0; background: var(--qkg-teal); }
.qkg-player-body { display: flex; align-items: center; gap: 14px 18px; flex-wrap: wrap; padding: 10px 18px 12px 18px; }
.qkg-player-title { display: flex; flex-direction: column; gap: 2px; min-width: 200px; flex: 1 1 220px; }
.qkg-player-name { display: flex; align-items: baseline; gap: 10px; cursor: pointer; }
.qkg-player-name:hover .qkg-player-name-en { text-decoration: underline; }
.qkg-player-name-ar { font-size: 26px; line-height: 1.3; color: var(--qkg-teal-strong); }
.qkg-player-name-en { font-size: 15px; font-weight: 600; color: var(--qkg-ink); }
.qkg-player-sub { display: flex; gap: 10px; font-size: 12px; color: var(--qkg-ink-3); }
.qkg-player-where { font-weight: 600; color: var(--qkg-ink-2); }
.qkg-player-transport, .qkg-player-tools { display: inline-flex; align-items: center; gap: 6px; }
.qkg-player-play { width: 44px; height: 44px; font-size: 16px; }
.qkg-player-reciter { display: flex; flex-direction: column; align-items: flex-end; line-height: 1.25; flex: 0 1 auto; min-width: 0; }
.qkg-player-reciter-ar { font-size: 16px; color: var(--qkg-ink-2); }
.qkg-player-reciter-en { font-size: 11px; color: var(--qkg-ink-3); white-space: nowrap; }
body.qkg-player-on .qkg-column { padding-bottom: 120px; }
body.qkg-player-on .qkg-reader { padding-bottom: 180px; }
body.qkg-player-on .qkg-study-body { padding-bottom: 110px; }
@media (max-width: 760px) { .qkg-player-reciter { display: none; } .qkg-player-dock { left: 8px; right: 8px; bottom: 8px; } }
";
            document.head.appendChild(style);
        }
    }
}
