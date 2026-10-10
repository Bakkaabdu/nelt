/* Survival German — أنقذ نفسك بالألمانية
   A story game for A1/A2 learners. Plays wwwroot/game/missions/m<N>.<level>.json (format: tools/survival-german/SCHEMA.md).
   Everything runs in the browser; progress is kept in localStorage (and in memory when storage is unavailable). */
(() => {
    "use strict";

    const root = document.getElementById("sg");
    if (!root) {
        return;
    }

    const BASE = root.dataset.missions || "/game/missions/";
    const STORE_KEY = "nelt.survival-german.v1";
    const LEVELS = ["A1", "A2"];
    const CHALLENGES = new Set(["choice", "listen", "build", "fill", "match", "write", "find"]);

    // ------------------------------------------------------------------ static content

    const LEVEL_INFO = {
        A1: {
            name: "Beginner Survival",
            ar: "النجاة للمبتدئين",
            points: [
                "جمل قصيرة وبسيطة وكلمات الحياة اليومية الأساسية",
                "التحيات، الأرقام، الأسعار، الأوقات والتواريخ",
                "زمن المضارع وأسئلة W البسيطة (Wo? Was? Wie viel?)",
                "شرح عربي سخي ومحادثات قصيرة تكبر تدريجيًا",
            ],
        },
        A2: {
            name: "Everyday Life in Germany",
            ar: "الحياة اليومية في ألمانيا",
            points: [
                "حوارات أطول ومواقف أكثر تفصيلًا ومفردات أوسع",
                "الماضي التام (Perfekt): Ich habe … gekauft / Ich bin … gefahren",
                "الأفعال المساعدة والطلبات المهذبة: Könnten Sie …? Ich hätte gern …",
                "جمل فرعية بسيطة مع weil و dass و wenn، وأسئلة توضيح ومتابعة",
            ],
        },
    };

    const MISSIONS = [
        { id: "m1", icon: "✈️", stop: "Flughafen BER", city: "Berlin", scene: "airport",
          title: { A1: ["Ankunft am Flughafen", "الوصول إلى المطار"], A2: ["Ankunft am Flughafen", "الوصول إلى المطار"] } },
        { id: "m2", icon: "🚄", stop: "Berlin Hbf", city: "Berlin → München", scene: "train",
          title: { A1: ["Eine Fahrkarte nach München", "تذكرة إلى ميونخ"], A2: ["Mit dem ICE nach München", "من برلين إلى ميونخ بالقطار السريع"] } },
        { id: "m3", icon: "🛒", stop: "Frischmarkt", city: "München-Schwabing", scene: "market",
          title: { A1: ["Einkaufen im Supermarkt", "التسوّق في السوبرماركت"], A2: ["Großeinkauf für das Abendessen", "تسوّق كبير لعشاء الليلة"] } },
        { id: "m4", icon: "🍽️", stop: "Gasthaus Zum Goldenen Hirsch", city: "Marienplatz", scene: "restaurant",
          title: { A1: ["Im Restaurant", "في المطعم"], A2: ["Ein Abend im Gasthaus", "أمسية في المطعم البافاري"] } },
        { id: "m5", icon: "🩺", stop: "Praxis Dr. Keller", city: "Leopoldstraße", scene: "doctor",
          title: { A1: ["Beim Arzt", "عند الطبيب"], A2: ["Krank in München", "مريض في ميونخ"] } },
        { id: "m6", icon: "💼", stop: "Café Sonnenschein", city: "München", scene: "office",
          title: { A1: ["Das Vorstellungsgespräch", "مقابلة العمل"], A2: ["Das Vorstellungsgespräch", "مقابلة العمل"] } },
        { id: "m7", icon: "🏠", stop: "Ein neues Zuhause", city: "München", scene: "home",
          title: { A1: ["Ein Zimmer in München", "غرفة في ميونخ"], A2: ["Ein neues Zuhause in München", "بيت جديد في ميونخ"] } },
    ];

    const ACHIEVEMENTS = [
        { id: "m1", icon: "👣", name: "First Steps in Germany", ar: "أولى خطواتك في ألمانيا", how: "أكمل مهمة المطار" },
        { id: "m2", icon: "🎫", name: "Train Ticket Expert", ar: "خبير تذاكر القطار", how: "أكمل مهمة تذكرة القطار" },
        { id: "m3", icon: "🛒", name: "Smart Shopper", ar: "المتسوّق الذكي", how: "أكمل مهمة السوبرماركت" },
        { id: "m4", icon: "🍽️", name: "Restaurant Regular", ar: "زبون المطعم الدائم", how: "أكمل مهمة المطعم" },
        { id: "m5", icon: "🗣️", name: "Everyday Communicator", ar: "متواصل في الحياة اليومية", how: "أكمل مهمة الطبيب" },
        { id: "m6", icon: "💼", name: "Interview Ready", ar: "جاهز للمقابلة", how: "أكمل مهمة مقابلة العمل" },
        { id: "m7", icon: "🏠", name: "Finding a Home", ar: "وجدتُ بيتًا", how: "أكمل مهمة البحث عن سكن" },
        { id: "flawless", icon: "💎", name: "Flawless", ar: "بلا أخطاء", how: "أكمل مهمة دون أن تفقد أي قلب" },
        { id: "self", icon: "🧭", name: "On My Own", ar: "بلا مساعدة", how: "أكمل مهمة دون أي تلميح" },
        { id: "comeback", icon: "📈", name: "Comeback", ar: "العودة الأقوى", how: "أعد مهمة واحصل على نتيجة أفضل من السابقة" },
        { id: "both", icon: "🎓", name: "Two Levels", ar: "على المستويين", how: "أكمل المهمة نفسها في A1 و A2" },
        { id: "journeyA1", icon: "🗺️", name: "Survivor A1", ar: "ناجٍ في A1", how: "أكمل المهمات السبع في A1" },
        { id: "journeyA2", icon: "🏆", name: "Survivor A2", ar: "ناجٍ في A2", how: "أكمل المهمات السبع في A2" },
        { id: "stars", icon: "🌟", name: "Star Collector", ar: "جامع النجوم", how: "اجمع 3 نجوم في مهمة واحدة" },
    ];

    // de: name in German; from: "aus …" with the right article; ar: Arabic name; flag.
    const COUNTRIES = [
        ["Libyen", "aus Libyen", "ليبيا", "🇱🇾"],
        ["Ägypten", "aus Ägypten", "مصر", "🇪🇬"],
        ["Algerien", "aus Algerien", "الجزائر", "🇩🇿"],
        ["Bahrain", "aus Bahrain", "البحرين", "🇧🇭"],
        ["Irak", "aus dem Irak", "العراق", "🇮🇶"],
        ["Jemen", "aus dem Jemen", "اليمن", "🇾🇪"],
        ["Jordanien", "aus Jordanien", "الأردن", "🇯🇴"],
        ["Katar", "aus Katar", "قطر", "🇶🇦"],
        ["Kuwait", "aus Kuwait", "الكويت", "🇰🇼"],
        ["Libanon", "aus dem Libanon", "لبنان", "🇱🇧"],
        ["Marokko", "aus Marokko", "المغرب", "🇲🇦"],
        ["Mauretanien", "aus Mauretanien", "موريتانيا", "🇲🇷"],
        ["Oman", "aus dem Oman", "عُمان", "🇴🇲"],
        ["Palästina", "aus Palästina", "فلسطين", "🇵🇸"],
        ["Saudi-Arabien", "aus Saudi-Arabien", "السعودية", "🇸🇦"],
        ["Somalia", "aus Somalia", "الصومال", "🇸🇴"],
        ["Sudan", "aus dem Sudan", "السودان", "🇸🇩"],
        ["Syrien", "aus Syrien", "سوريا", "🇸🇾"],
        ["Tunesien", "aus Tunesien", "تونس", "🇹🇳"],
        ["Vereinigte Arabische Emirate", "aus den Vereinigten Arabischen Emiraten", "الإمارات", "🇦🇪"],
        ["Dschibuti", "aus Dschibuti", "جيبوتي", "🇩🇯"],
        ["Komoren", "aus den Komoren", "جزر القمر", "🇰🇲"],
    ].map(([de, from, ar, flag]) => ({ de, from, ar, flag }));

    const SKILL_LABEL = {
        dialogue: "حوار", reading: "قراءة", listening: "استماع", vocab: "مفردات",
        grammar: "قواعد", numbers: "أرقام", writing: "كتابة",
    };
    const TYPE_LABEL = {
        choice: "اختر الجواب", listen: "استمع", build: "رتّب الجملة", fill: "أكمل الفراغ",
        match: "طابِق", write: "اكتب بالألمانية", find: "ابحث واختر",
    };
    const DOC_LABEL = {
        sign: "لافتة", board: "لوحة المواعيد", ticket: "تذكرة", ad: "إعلان", menu: "قائمة الطعام", receipt: "إيصال",
        message: "رسالة", list: "قائمة", form: "استمارة", announcement: "إعلان صوتي", screen: "شاشة", letter: "رسالة / خطاب",
    };

    const POINTS = { base: 10, bonus: 20, final: 30, noHint: 5, perfect: 50, selfMade: 30, improved: 25 };

    // ------------------------------------------------------------------ storage

    const memory = {};
    const store = {
        load() {
            try {
                const raw = window.localStorage.getItem(STORE_KEY);
                if (raw) {
                    return JSON.parse(raw);
                }
            } catch {
                /* private mode, blocked storage, bad JSON */
            }
            return memory.data || null;
        },
        save(data) {
            memory.data = data;
            try {
                window.localStorage.setItem(STORE_KEY, JSON.stringify(data));
            } catch {
                /* keep the in-memory copy */
            }
        },
    };

    const fresh = () => ({ profile: null, level: "A1", points: 0, progress: { A1: {}, A2: {} }, achievements: {}, sound: true });
    let data = Object.assign(fresh(), store.load() || {});
    data.progress = Object.assign({ A1: {}, A2: {} }, data.progress || {});
    data.achievements = data.achievements || {};
    const save = () => store.save(data);

    // ------------------------------------------------------------------ helpers

    const ESC = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
    const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ESC[c]);

    const country = () => COUNTRIES.find((c) => c.de === data.profile?.country) || COUNTRIES[0];

    /** Replaces {name}, {country}, {fromCountry}, {countryAr} with the player's details. */
    const fill = (s) => {
        if (typeof s !== "string") {
            return s;
        }
        const c = country();
        return s
            .replace(/\{name\}/g, data.profile?.name || "Sami")
            .replace(/\{fromCountry\}/g, c.from)
            .replace(/\{countryAr\}/g, c.ar)
            .replace(/\{country\}/g, c.de);
    };

    // Latin runs inside Arabic text (German words, numbers, prices) are isolated so punctuation stays put.
    const LATIN_RUN = /[A-Za-zÄÖÜäöüß0-9€][A-Za-zÄÖÜäöüß0-9€ ,.:;?!'’„“\-–/+%&…]*[A-Za-zÄÖÜäöüß0-9€?!%…]|[A-Za-zÄÖÜäöüß0-9€]/g;

    /** Arabic rich text: escapes, **bold**, line breaks, isolated German runs. */
    const ar = (s) => {
        const parts = fill(String(s ?? "")).split("**");
        return parts.map((part, i) => {
            let out = "";
            let last = 0;
            part.replace(LATIN_RUN, (m, offset) => {
                out += esc(part.slice(last, offset)) + `<bdi class="sg-de-in" lang="de" dir="ltr">${esc(m)}</bdi>`;
                last = offset + m.length;
                return m;
            });
            out += esc(part.slice(last));
            out = out.replace(/\n/g, "<br>");
            return i % 2 === 1 ? `<strong>${out}</strong>` : out;
        }).join("");
    };

    /** German text, left-to-right. */
    const de = (s, cls = "") => `<span class="sg-de ${cls}" lang="de" dir="ltr">${esc(fill(s))}</span>`;

    const shuffle = (arr) => {
        const a = arr.slice();
        for (let i = a.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [a[i], a[j]] = [a[j], a[i]];
        }
        return a;
    };

    /** Lenient comparison for typed answers: case, punctuation, spacing and umlaut spellings don't matter. */
    const norm = (s) => fill(String(s))
        .toLowerCase()
        .replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss")
        .replace(/€/g, " euro ")
        .replace(/(\d),(\d)/g, "$1.$2")
        .replace(/[^a-z0-9.:\s]/g, " ")
        .replace(/(?<!\d)[.:]|[.:](?!\d)/g, " ")
        .replace(/\s+/g, " ")
        .trim();

    const stars = (n, max = 3) => Array.from({ length: max }, (_, i) =>
        `<span class="sg-star ${i < n ? "is-on" : ""}" aria-hidden="true">★</span>`).join("");

    const hearts = (n) => Array.from({ length: 3 }, (_, i) =>
        `<span class="sg-heart ${i < n ? "is-on" : ""}" aria-hidden="true">${i < n ? "❤" : "♡"}</span>`).join("");

    const $ = (sel, el = root) => el.querySelector(sel);
    const $$ = (sel, el = root) => Array.from(el.querySelectorAll(sel));

    // ------------------------------------------------------------------ speech (German text-to-speech)

    const speech = {
        supported: "speechSynthesis" in window && typeof window.SpeechSynthesisUtterance === "function",
        voice: null,
        pickVoice() {
            if (!this.supported) {
                return;
            }
            const voices = window.speechSynthesis.getVoices();
            this.voice = voices.find((v) => /^de(-|_)DE/i.test(v.lang) && /google|natural|online/i.test(v.name))
                || voices.find((v) => /^de(-|_)DE/i.test(v.lang))
                || voices.find((v) => /^de/i.test(v.lang))
                || null;
        },
        speak(text, slow = false) {
            if (!this.supported || !text) {
                return false;
            }
            try {
                const synth = window.speechSynthesis;
                synth.cancel();
                const u = new SpeechSynthesisUtterance(fill(text).replace(/\s\|\s/g, ", "));
                u.lang = "de-DE";
                if (this.voice) {
                    u.voice = this.voice;
                }
                const base = currentLevel() === "A1" ? 0.85 : 0.95;
                u.rate = slow ? 0.6 : base;
                synth.speak(u);
                return true;
            } catch {
                return false;
            }
        },
    };
    if (speech.supported) {
        speech.pickVoice();
        window.speechSynthesis.addEventListener?.("voiceschanged", () => speech.pickVoice());
    }

    // ------------------------------------------------------------------ recorded speech (neural voices, tools/survival-german/make_audio.py)

    const AUDIO_BASE = root.dataset.audio || BASE.replace(/missions\/?$/, "audio/");
    const audioMap = {};
    let playing = null;

    const clipFor = (raw, who) => {
        if (!raw) {
            return null;
        }
        const key = `${who}|${raw}`;
        return audioMap[key] || (/\{(country|fromCountry)\}/.test(raw) ? audioMap[`${key}|${country().de}`] : null) || null;
    };

    const stopVoice = () => {
        if (playing) {
            playing.onerror = null;
            playing.pause();
            playing = null;
        }
        if (speech.supported) {
            window.speechSynthesis.cancel();
        }
    };

    /** Plays the recorded clip for this line (who = speaker key), or falls back to the browser voice. */
    function say(raw, who = "_", slow = false) {
        if (!raw) {
            return false;
        }
        stopVoice();
        const id = clipFor(raw, who);
        if (!id) {
            return speech.speak(raw, slow);
        }
        const a = new Audio(`${AUDIO_BASE}${id}.mp3`);
        a.preservesPitch = true;
        a.playbackRate = slow ? 0.72 : 1;
        a.onerror = () => {
            if (playing === a) {
                playing = null;
                speech.speak(raw, slow);
            }
        };
        playing = a;
        a.play().catch((e) => {
            if (playing === a && e && e.name !== "AbortError") {
                playing = null;
                speech.speak(raw, slow);
            }
        });
        return true;
    }

    const canSay = (raw, who) => Boolean(clipFor(raw, who)) || speech.supported;

    const speakBtn = (text, who = "_", label = "استمع") => canSay(text, who)
        ? `<button type="button" class="sg-say" data-say="${esc(text)}" data-who="${esc(who)}" title="${label}" aria-label="${label}">🔊</button>`
        : "";

    // ------------------------------------------------------------------ progress queries

    const currentLevel = () => (LEVELS.includes(data.level) ? data.level : "A1");
    const progressOf = (level, id) => data.progress[level]?.[id] || null;
    const isDone = (level, id) => Boolean(progressOf(level, id)?.done);
    const isUnlocked = (level, index) => index === 0 || isDone(level, MISSIONS[index - 1].id);
    const levelStars = (level) => MISSIONS.reduce((sum, m) => sum + (progressOf(level, m.id)?.stars || 0), 0);
    const levelDone = (level) => MISSIONS.filter((m) => isDone(level, m.id)).length;

    // ------------------------------------------------------------------ toasts

    const toast = (html, kind = "") => {
        let stack = $(".sg-toasts");
        if (!stack) {
            stack = document.createElement("div");
            stack.className = "sg-toasts";
            stack.setAttribute("aria-live", "polite");
            root.appendChild(stack);
        }
        const t = document.createElement("div");
        t.className = `sg-toast ${kind}`;
        t.innerHTML = html;
        stack.appendChild(t);
        setTimeout(() => t.classList.add("is-out"), 2200);
        setTimeout(() => t.remove(), 2700);
    };

    // ------------------------------------------------------------------ screens

    let screen = "";
    const show = (name, html) => {
        screen = name;
        stopVoice();
        root.innerHTML = `<div class="sg-screen sg-screen--${name}">${html}</div>`;
        document.body.classList.toggle("sg-playing", name === "play");
        root.scrollIntoView({ block: "start" });
        const focus = $("[data-autofocus]");
        focus?.focus({ preventScroll: true });
    };

    const brand = () => `
        <div class="sg-brand">
            <span class="sg-brand__mark" aria-hidden="true"><span></span><span></span><span></span></span>
            <span class="sg-brand__name" lang="en" dir="ltr">Survival German</span>
            <span class="sg-brand__ar">أنقذ نفسك بالألمانية</span>
        </div>`;

    // ---------- welcome / profile

    function renderWelcome() {
        const p = data.profile || {};
        const level = currentLevel();
        const options = COUNTRIES.map((c) =>
            `<option value="${esc(c.de)}" ${c.de === (p.country || "Libyen") ? "selected" : ""}>${c.flag} ${esc(c.ar)}</option>`).join("");
        const levelCard = (code) => `
            <label class="sg-levelpick ${code === level ? "is-on" : ""}">
                <input type="radio" name="level" value="${code}" ${code === level ? "checked" : ""}>
                <span class="sg-levelpick__code">${code}</span>
                <span class="sg-levelpick__name" lang="en" dir="ltr">${LEVEL_INFO[code].name}</span>
                <span class="sg-levelpick__ar">${LEVEL_INFO[code].ar}</span>
                <ul>${LEVEL_INFO[code].points.map((x) => `<li>${ar(x)}</li>`).join("")}</ul>
            </label>`;

        show("welcome", `
            <section class="sg-hero">
                ${brand()}
                <div class="sg-hero__board" aria-hidden="true">
                    <div class="sg-flap"><span>LANDUNG</span><span>BER</span><span>14:05</span></div>
                    <div class="sg-flap"><span>ZIEL</span><span>MÜNCHEN</span><span>?</span></div>
                </div>
                <h1 class="sg-hero__title">رحلتك في ألمانيا تبدأ الآن.</h1>
                <p class="sg-hero__lead">
                    هبطت طائرتك للتو في مطار برلين. لا تعرف من الألمانية إلا القليل، وصديقتك لينا تنتظرك في ميونخ.
                    في سبع مهمات ستتكلم مع الناس، وتقرأ اللافتات، وتشتري التذاكر والطعام، وتزور الطبيب، وتجتاز مقابلة عمل،
                    وتجد بيتًا جديدًا — كل ذلك بالألمانية.
                </p>
                <ul class="sg-rules">
                    <li><span>❤❤❤</span> ثلاثة قلوب في كل مهمة. الخطأ يُشرح لك ولا ينهي المغامرة.</li>
                    <li><span>💡</span> التلميحات والترجمة متاحة دائمًا، لكن جرّب أولًا وحدك.</li>
                    <li><span>★★★</span> نجوم ونقاط وإنجازات — وإعادة المهمة لتحسين نتيجتك.</li>
                </ul>
            </section>
            <form class="sg-card sg-setup" data-form="profile" autocomplete="off">
                <h2>من أنت؟</h2>
                <div class="sg-setup__row">
                    <label class="sg-field">
                        <span>اسمك (بالحروف اللاتينية)</span>
                        <input name="name" lang="de" dir="ltr" maxlength="20" required value="${esc(p.name || "")}" placeholder="z. B. Omar" data-autofocus>
                        <small>سيناديك به الناس في القصة: «Hallo Omar!»</small>
                    </label>
                    <label class="sg-field">
                        <span>من أي بلد أنت؟</span>
                        <select name="country">${options}</select>
                        <small>ستقول في المطار: «Ich komme aus …»</small>
                    </label>
                </div>
                <h2>اختر مستواك</h2>
                <div class="sg-levelpicks">${levelCard("A1")}${levelCard("A2")}</div>
                <p class="sg-note">يمكنك تغيير المستوى في أي وقت من خريطة الرحلة، ويُحفظ تقدّمك في كل مستوى على حدة.</p>
                <button class="sg-btn sg-btn--go" type="submit">${p.name ? "حفظ والعودة إلى الخريطة" : "ابدأ الرحلة ✈️"}</button>
            </form>`);
    }

    // ---------- journey map

    function renderMap() {
        const level = currentLevel();
        const c = country();
        const done = levelDone(level);
        const earned = ACHIEVEMENTS.filter((a) => data.achievements[a.id]).length;

        const stations = MISSIONS.map((m, i) => {
            const p = progressOf(level, m.id);
            const open = isUnlocked(level, i);
            const [tDe, tAr] = m.title[level];
            const state = p?.done ? "done" : open ? "open" : "locked";
            const action = !open
                ? `<span class="sg-station__lock">🔒 أكمل المهمة ${i} أولًا</span>`
                : `<button type="button" class="sg-btn ${p?.done ? "sg-btn--ghost" : "sg-btn--go"} sg-btn--sm" data-action="brief" data-mission="${m.id}">
                        ${p?.done ? "العب مجددًا ↺" : p?.plays ? "حاول مرة أخرى ▶" : "ابدأ المهمة ▶"}</button>`;
            return `
                <li class="sg-station is-${state}" style="--i:${i}">
                    <span class="sg-station__dot" aria-hidden="true">${p?.done ? "✓" : i + 1}</span>
                    <article class="sg-station__card">
                        <div class="sg-station__icon sg-art sg-art--${m.scene}" aria-hidden="true"><span>${m.icon}</span></div>
                        <div class="sg-station__body">
                            <p class="sg-station__stop"><span lang="de" dir="ltr">${esc(m.stop)}</span> · <span lang="de" dir="ltr">${esc(m.city)}</span></p>
                            <h3>${esc(tAr)}</h3>
                            <p class="sg-station__de">${de(tDe)}</p>
                            ${p?.done ? `<p class="sg-station__result"><span class="sg-stars">${stars(p.stars)}</span> أفضل نتيجة: <b>${p.best}</b> نقطة</p>` : ""}
                        </div>
                        <div class="sg-station__go">${action}</div>
                    </article>
                </li>`;
        }).join("");

        const ach = ACHIEVEMENTS.map((a) => {
            const on = Boolean(data.achievements[a.id]);
            return `<li class="sg-badge ${on ? "is-on" : ""}" title="${esc(a.how)}">
                        <span class="sg-badge__icon" aria-hidden="true">${on ? a.icon : "🔒"}</span>
                        <span class="sg-badge__name" lang="en" dir="ltr">${esc(a.name)}</span>
                        <span class="sg-badge__ar">${esc(a.ar)}</span>
                        <span class="sg-badge__how">${esc(a.how)}</span>
                    </li>`;
        }).join("");

        show("map", `
            <header class="sg-top">
                ${brand()}
                <div class="sg-top__who">
                    <span class="sg-avatar" aria-hidden="true">${c.flag}</span>
                    <span><b lang="de" dir="ltr">${esc(data.profile.name)}</b><small>${esc(c.ar)} → ألمانيا</small></span>
                    <button type="button" class="sg-link" data-action="profile">تعديل</button>
                </div>
            </header>

            <div class="sg-levels" role="tablist" aria-label="المستوى">
                ${LEVELS.map((l) => `
                    <button type="button" role="tab" aria-selected="${l === level}" class="sg-levels__tab ${l === level ? "is-on" : ""}" data-action="level" data-level="${l}">
                        <b>${l}</b><span lang="en" dir="ltr">${LEVEL_INFO[l].name}</span>
                        <small>${levelDone(l)}/7 · ${levelStars(l)}★</small>
                    </button>`).join("")}
            </div>
            <p class="sg-levels__about">${ar(LEVEL_INFO[level].points.join(" · "))}</p>

            <div class="sg-stats">
                <div><b>${data.points}</b><span>مجموع النقاط</span></div>
                <div><b>${done}/7</b><span>مهمات مكتملة (${level})</span></div>
                <div><b>${levelStars(level)}/21</b><span>نجوم (${level})</span></div>
                <div><b>${earned}/${ACHIEVEMENTS.length}</b><span>إنجازات</span></div>
            </div>

            <section class="sg-journey">
                <h2>خريطة الرحلة <small>من برلين إلى بيتك الجديد في ميونخ</small></h2>
                <div class="sg-journey__progress" aria-hidden="true"><span style="width:${(done / 7) * 100}%"></span></div>
                <ol class="sg-route">${stations}</ol>
            </section>

            <section class="sg-achievements">
                <h2>الإنجازات</h2>
                <ul class="sg-badges">${ach}</ul>
            </section>

            <footer class="sg-foot">
                <button type="button" class="sg-link" data-action="sound">${data.sound ? "🔊 الصوت مفعّل" : "🔇 الصوت متوقف"}</button>
                <button type="button" class="sg-link sg-link--danger" data-action="reset">مسح تقدّمي والبدء من جديد</button>
            </footer>`);
    }

    // ---------- mission loading

    const cache = {};
    async function loadMission(id, level) {
        const key = `${id}.${level.toLowerCase()}`;
        if (!cache[key]) {
            const res = await fetch(`${BASE}${key}.json`, { headers: { Accept: "application/json" } });
            if (!res.ok) {
                throw new Error(`HTTP ${res.status}`);
            }
            cache[key] = await res.json();
            try {
                const voices = await fetch(`${BASE}${key}.audio.json`, { headers: { Accept: "application/json" } });
                if (voices.ok) {
                    Object.assign(audioMap, await voices.json());
                }
            } catch {
                /* no recordings: the browser voice is used */
            }
        }
        return cache[key];
    }

    async function openBriefing(id) {
        show("loading", `<div class="sg-loading"><span class="sg-spinner" aria-hidden="true"></span><p>جارٍ تجهيز المهمة…</p></div>`);
        try {
            const m = await loadMission(id, currentLevel());
            renderBriefing(m);
        } catch (e) {
            show("error", `<div class="sg-card sg-error"><h2>تعذّر تحميل المهمة</h2><p>تحقق من الاتصال ثم حاول مرة أخرى.</p>
                <p class="sg-note" dir="ltr">${esc(e.message)}</p>
                <button class="sg-btn sg-btn--go" data-action="brief" data-mission="${esc(id)}">إعادة المحاولة</button>
                <button class="sg-btn sg-btn--ghost" data-action="map">الخريطة</button></div>`);
        }
    }

    function renderBriefing(m) {
        const meta = MISSIONS.find((x) => x.id === m.id);
        const index = MISSIONS.indexOf(meta);
        const p = progressOf(m.level, m.id);
        const total = countChallenges(m);
        const cast = Object.values(m.cast || {}).map((c) =>
            `<li><span class="sg-avatar" aria-hidden="true">${esc(c.avatar || "🙂")}</span><span><b lang="de" dir="ltr">${esc(c.name)}</b><small>${ar(c.role)}</small></span></li>`).join("");
        const preview = (m.vocab || []).slice(0, 10).map((v) => `<li>${de(v.de)}</li>`).join("");

        show("brief", `
            <header class="sg-top">
                <button type="button" class="sg-back" data-action="map">→ الخريطة</button>
                ${brand()}
            </header>
            <article class="sg-brief">
                <div class="sg-brief__art sg-art sg-art--${meta.scene}" aria-hidden="true"><span>${meta.icon}</span></div>
                <div class="sg-brief__main">
                    <p class="sg-kicker">المهمة ${index + 1} من 7 · ${m.level} · <span lang="de" dir="ltr">${esc(m.place)}</span></p>
                    <h1>${esc(m.title.ar)}</h1>
                    <p class="sg-brief__de">${de(m.title.de)}</p>
                    <p class="sg-brief__intro">${ar(m.intro)}</p>
                    <div class="sg-objective"><b>🎯 هدفك</b><p>${ar(m.objective)}</p></div>
                </div>
            </article>
            <div class="sg-brief__grid">
                <section class="sg-card"><h2>الشخصيات</h2><ul class="sg-cast">${cast}</ul></section>
                <section class="sg-card"><h2>كلمات ستقابلها</h2><ul class="sg-chips">${preview}</ul>
                    <p class="sg-note">${(m.vocab || []).length} كلمة و${(m.expressions || []).length} عبارة مهمة في دفتر المهمة 📒</p></section>
                <section class="sg-card"><h2>القواعد</h2>
                    <ul class="sg-rules sg-rules--compact">
                        <li><span>🧩</span> نحو ${total} تحديًا في ${m.scenes.length} مشاهد (10–20 دقيقة)</li>
                        <li><span>❤</span> 3 قلوب. أكمل مشهدًا بلا خطأ لتستعيد قلبًا.</li>
                        <li><span>💡</span> التلميح لا يكلّف قلبًا، لكن الإجابة بدونه تعطي +${POINTS.noHint}</li>
                        <li><span>★</span> نجمة للإكمال، ونجمة لدقة 70٪، ونجمة لدقة 90٪ مع نجاح التحدي النهائي</li>
                    </ul></section>
            </div>
            ${p?.done ? `<p class="sg-best">أفضل نتيجة سابقة: <span class="sg-stars">${stars(p.stars)}</span> ${p.best} نقطة. حسّنها لتحصل على مكافأة!</p>` : ""}
            <div class="sg-actions">
                <button type="button" class="sg-btn sg-btn--go" data-action="start" data-autofocus>انطلق ▶</button>
            </div>`);
        briefing = m;
    }

    let briefing = null;

    // ------------------------------------------------------------------ the mission run

    let run = null;

    const countChallenges = (m) => {
        let n = 0;
        const walk = (steps) => steps.forEach((s) => {
            if (CHALLENGES.has(s.type)) {
                n++;
            } else if (s.type === "decision") {
                n += Math.min(...s.options.map((o) => (o.then || []).filter((x) => CHALLENGES.has(x.type)).length));
            }
        });
        m.scenes.forEach((sc) => walk(sc.steps));
        return n;
    };

    /** Queue entries: { kind: "scene", scene, index } or { kind: "step", step, scene }. */
    const buildQueue = (m, fromScene = 0) => {
        const q = [];
        m.scenes.forEach((sc, i) => {
            if (i < fromScene) {
                return;
            }
            q.push({ kind: "scene", scene: sc, index: i });
            sc.steps.forEach((step) => q.push({ kind: "step", step, scene: i }));
        });
        return q;
    };

    function startMission(m, fromScene = 0, snapshot = null) {
        const prev = progressOf(m.level, m.id);
        run = Object.assign({
            mission: m,
            meta: MISSIONS.find((x) => x.id === m.id),
            queue: buildQueue(m, fromScene),
            pos: -1,
            hearts: 3,
            points: 0,
            done: 0,
            firstTry: 0,
            hintsUsed: 0,
            heartsLost: 0,
            revealed: 0,
            finalFirstTry: false,
            revived: false,
            total: countChallenges(m),
            prevBest: prev?.best || 0,
            sceneIndex: fromScene,
            sceneMistakes: 0,
            sceneChallenges: 0,
            snapshot: null,
            waiting: null,
        }, snapshot || {});
        run.queue = buildQueue(m, fromScene);
        run.pos = -1;
        renderPlayShell();
        next();
    }

    function renderPlayShell() {
        const m = run.mission;
        show("play", `
            <header class="sg-hud">
                <button type="button" class="sg-back" data-action="quit" aria-label="الخروج إلى الخريطة">✕</button>
                <div class="sg-hud__title">
                    <b>${esc(m.title.ar)}</b>
                    <small><span class="sg-hud__scene"></span></small>
                </div>
                <div class="sg-hud__hearts" aria-live="polite"></div>
                <div class="sg-hud__points"><b>0</b><small>نقطة</small></div>
                <button type="button" class="sg-hud__book" data-action="book" aria-label="دفتر الكلمات">📒</button>
            </header>
            <div class="sg-progress" aria-hidden="true">
                <div class="sg-progress__bar"><span></span></div>
                <ol class="sg-progress__scenes">${m.scenes.map((s, i) => `<li data-scene="${i}">${esc(s.title.ar)}</li>`).join("")}</ol>
            </div>
            <div class="sg-stage">
                <div class="sg-feed" aria-live="polite"></div>
            </div>
            <div class="sg-dock"></div>
            <aside class="sg-book" hidden></aside>`);
        updateHud();
    }

    function updateHud() {
        if (!run || screen !== "play") {
            return;
        }
        $(".sg-hud__hearts").innerHTML = hearts(run.hearts);
        $(".sg-hud__hearts").setAttribute("aria-label", `${run.hearts} قلوب متبقية`);
        $(".sg-hud__points b").textContent = String(run.points);
        const sc = run.mission.scenes[run.sceneIndex];
        $(".sg-hud__scene").textContent = `المشهد ${run.sceneIndex + 1}/${run.mission.scenes.length} · ${sc ? sc.title.ar : ""} · التحدي ${Math.min(run.done + 1, run.total)}/${run.total}`;
        $(".sg-progress__bar span").style.width = `${Math.min(100, (run.done / Math.max(1, run.total)) * 100)}%`;
        $$(".sg-progress__scenes li").forEach((li) => {
            const i = Number(li.dataset.scene);
            li.className = i < run.sceneIndex ? "is-done" : i === run.sceneIndex ? "is-on" : "";
        });
    }

    const feed = () => $(".sg-feed");
    const dock = () => $(".sg-dock");

    function append(html, cls = "") {
        const el = document.createElement("div");
        el.className = `sg-item ${cls}`;
        el.innerHTML = html;
        feed().appendChild(el);
        requestAnimationFrame(() => el.classList.add("is-in"));
        el.scrollIntoView({ behavior: "smooth", block: "end" });
        return el;
    }

    function continueButton(label = "متابعة") {
        dock().innerHTML = `<button type="button" class="sg-btn sg-btn--go sg-btn--wide" data-action="next">${label} <span aria-hidden="true">←</span></button>`;
        run.waiting = "next";
    }

    function next() {
        if (!run) {
            return;
        }
        run.pos++;
        const entry = run.queue[run.pos];
        dock().innerHTML = "";
        run.waiting = null;
        if (!entry) {
            finishMission();
            return;
        }
        if (entry.kind === "scene") {
            beginScene(entry);
            return;
        }
        playStep(entry.step);
    }

    function beginScene(entry) {
        // A clean scene earns a heart back.
        if (entry.index > run.sceneIndex && run.sceneChallenges > 0 && run.sceneMistakes === 0 && run.hearts < 3) {
            run.hearts++;
            toast("❤ مشهد بلا أخطاء — استعدت قلبًا!", "is-good");
        }
        run.sceneIndex = entry.index;
        run.sceneMistakes = 0;
        run.sceneChallenges = 0;
        run.snapshot = {
            points: run.points, done: run.done, firstTry: run.firstTry, hintsUsed: run.hintsUsed,
            heartsLost: run.heartsLost, revealed: run.revealed, finalFirstTry: run.finalFirstTry, total: run.total,
        };
        feed().innerHTML = "";
        const sc = entry.scene;
        append(`
            <div class="sg-scene sg-art sg-art--${run.meta.scene}">
                <span class="sg-scene__no">المشهد ${entry.index + 1}</span>
                <h2>${esc(sc.title.ar)}</h2>
                <p>${de(sc.title.de)}</p>
            </div>`, "sg-item--scene");
        updateHud();
        continueButton("ابدأ المشهد");
    }

    // ---------- story steps

    function speaker(who) {
        if (who === "me") {
            const c = country();
            return { name: data.profile?.name || "أنت", role: "أنت", avatar: c.flag, me: true };
        }
        return run.mission.cast?.[who] || { name: who, role: "", avatar: "🙂" };
    }

    function lineHtml(line, opts = {}) {
        const s = speaker(line.who);
        const trId = `tr-${Math.random().toString(36).slice(2, 9)}`;
        return `
            <div class="sg-line ${s.me ? "is-me" : ""}">
                <span class="sg-line__avatar" aria-hidden="true">${esc(s.avatar || "🙂")}</span>
                <div class="sg-line__bubble">
                    <span class="sg-line__who"><b lang="de" dir="ltr">${esc(fill(s.name))}</b>${s.role && !s.me ? ` · ${ar(s.role)}` : ""}</span>
                    <p>${de(line.de)} ${speakBtn(line.de, line.who)}</p>
                    ${line.ar && !opts.noTranslation ? `
                        <button type="button" class="sg-reveal" data-reveal="${trId}">ترجمة</button>
                        <p class="sg-tr" id="${trId}" hidden>${ar(line.ar)}</p>` : ""}
                </div>
            </div>`;
    }

    function docHtml(doc) {
        const kind = doc.kind || "sign";
        const trId = `tr-${Math.random().toString(36).slice(2, 9)}`;
        const rawLines = doc.lines || [];
        const lines = rawLines.map(fill);
        let body;
        if (kind === "board" || (kind === "screen" && lines.some((l) => l.includes(" | ")))) {
            const rows = lines.map((l) => l.split(/\s\|\s/));
            const head = kind === "board" || /^(ab|zeit|abfahrt)/i.test(rows[0]?.[0] || "") ? rows.shift() : null;
            body = `<table class="sg-board__table">
                ${head ? `<thead><tr>${head.map((c) => `<th>${esc(c)}</th>`).join("")}</tr></thead>` : ""}
                <tbody>${rows.map((r) => `<tr>${r.map((c) => `<td>${esc(c)}</td>`).join("")}</tr>`).join("")}</tbody></table>`;
        } else if (kind === "receipt" || kind === "menu" || kind === "ticket" || kind === "form") {
            body = `<ul class="sg-doc__rows">${lines.map((l) => {
                const cols = l.split(/\s\|\s/);
                return `<li>${cols.map((c) => `<span>${esc(c)}</span>`).join("")}</li>`;
            }).join("")}</ul>`;
        } else if (kind === "announcement") {
            const text = rawLines.join(" ");
            const id = `ann-${Math.random().toString(36).slice(2, 9)}`;
            body = `
                <div class="sg-ann">
                    <span class="sg-ann__wave" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i></span>
                    ${canSay(text, "_ann") ? `<button type="button" class="sg-btn sg-btn--sm" data-say="${esc(text)}" data-who="_ann">▶ استمع للإعلان</button>
                    <button type="button" class="sg-btn sg-btn--sm sg-btn--ghost" data-say="${esc(text)}" data-who="_ann" data-slow="1">🐢 ببطء</button>` : ""}
                    <button type="button" class="sg-reveal" data-reveal="${id}">اعرض النص الألماني</button>
                </div>
                <div id="${id}" ${canSay(text, "_ann") ? "hidden" : ""}><p>${lines.map(esc).join("<br>")}</p></div>`;
        } else {
            body = lines.map((l) => `<p>${esc(l)}</p>`).join("");
        }
        return `
            <figure class="sg-doc sg-doc--${esc(kind)}">
                <figcaption><span class="sg-doc__kind">${esc(DOC_LABEL[kind] || "")}</span>${doc.title ? ` <b lang="de" dir="ltr">${esc(fill(doc.title))}</b>` : ""}</figcaption>
                <div class="sg-doc__body" lang="de" dir="ltr">${body}</div>
                ${doc.ar ? `<button type="button" class="sg-reveal" data-reveal="${trId}">ترجمة</button><p class="sg-tr" id="${trId}" hidden>${ar(doc.ar)}</p>` : ""}
            </figure>`;
    }

    function playStep(step) {
        switch (step.type) {
            case "narration":
                append(`<div class="sg-narration"><p>${ar(step.ar)}</p>${step.de ? `<p class="sg-narration__de">${de(step.de)} ${speakBtn(step.de, "_")}</p>` : ""}</div>`);
                continueButton();
                break;
            case "line":
                append(lineHtml(step));
                if (step.who !== "me" && data.sound) {
                    say(step.de, step.who);
                }
                continueButton();
                break;
            case "doc":
                append(docHtml(step));
                if (step.kind === "announcement" && data.sound) {
                    say((step.lines || []).join(" "), "_ann");
                }
                continueButton();
                break;
            case "learn":
                append(`
                    <div class="sg-learn">
                        <span class="sg-learn__tag">عبارة مفيدة 📌</span>
                        <p class="sg-learn__de">${de(step.de)} ${speakBtn(step.de)}</p>
                        <p class="sg-learn__ar">${ar(step.ar)}</p>
                        <p class="sg-learn__use"><b>متى نستعملها؟</b> ${ar(step.use)}</p>
                        <p class="sg-learn__ex">${de(step.example)} ${speakBtn(step.example, "_")}<br><span>${ar(step.exampleAr)}</span></p>
                    </div>`);
                continueButton();
                break;
            case "grammar":
                append(`
                    <div class="sg-grammar">
                        <span class="sg-learn__tag">قاعدة صغيرة 🧠</span>
                        <h3>${ar(step.title)}</h3>
                        <p>${ar(step.body)}</p>
                        <ul>${(step.examples || []).map((e) => `<li>${de(e.de)} ${speakBtn(e.de)}<span>${ar(e.ar)}</span></li>`).join("")}</ul>
                    </div>`);
                continueButton();
                break;
            case "decision":
                playDecision(step);
                break;
            default:
                if (CHALLENGES.has(step.type)) {
                    playChallenge(step);
                } else {
                    next();
                }
        }
    }

    function playDecision(step) {
        const el = append(`
            <div class="sg-decision">
                <span class="sg-learn__tag">قرارك 🔀</span>
                <p class="sg-decision__q">${ar(step.prompt)}</p>
                <div class="sg-decision__opts">
                    ${step.options.map((o, i) => `<button type="button" class="sg-opt" data-decide="${i}">
                        <span>${ar(o.ar)}</span>${o.de ? `<small>${de(o.de)}</small>` : ""}</button>`).join("")}
                </div>
            </div>`);
        run.waiting = "decision";
        run.decision = { step, el };
    }

    function decide(i) {
        const { step, el } = run.decision || {};
        if (!step) {
            return;
        }
        const option = step.options[i];
        $$(".sg-opt", el).forEach((b, j) => {
            b.disabled = true;
            b.classList.toggle("is-picked", j === i);
        });
        const then = (option.then || []).map((s) => ({ kind: "step", step: s, scene: run.sceneIndex }));
        run.queue.splice(run.pos + 1, 0, ...then);
        // Adjust the challenge estimate: countChallenges assumed the branch with the fewest challenges.
        const counts = step.options.map((o) => (o.then || []).filter((x) => CHALLENGES.has(x.type)).length);
        run.total += counts[i] - Math.min(...counts);
        run.decision = null;
        updateHud();
        next();
    }

    // ---------- challenges

    function challengeFrame(ch, inner) {
        const tag = [TYPE_LABEL[ch.type], ch.skill ? SKILL_LABEL[ch.skill] : null].filter(Boolean).join(" · ");
        const flags = `${ch.final ? `<span class="sg-flag sg-flag--final">التحدي النهائي 🏁</span>` : ""}${ch.bonus ? `<span class="sg-flag sg-flag--bonus">تحدٍّ إضافي ✨ نقاط مضاعفة</span>` : ""}`;
        return `
            <section class="sg-challenge ${ch.final ? "is-final" : ""} ${ch.bonus ? "is-bonus" : ""}" data-challenge="${esc(ch.id)}">
                <header class="sg-challenge__head"><span class="sg-challenge__tag">${tag}</span>${flags}</header>
                ${ch.npc ? lineHtml(ch.npc) : ""}
                ${ch.doc ? docHtml(ch.doc) : ""}
                ${inner}
                <div class="sg-feedback" aria-live="assertive"></div>
                <div class="sg-challenge__tools">
                    <button type="button" class="sg-tool" data-action="hint">💡 تلميح</button>
                </div>
            </section>`;
    }

    function playChallenge(ch) {
        run.ch = { step: ch, attempts: 0, hint: false, solved: false, el: null, state: {} };
        let inner = "";
        switch (ch.type) {
            case "choice":
            case "listen": {
                const opts = shuffle(ch.options.map((o, i) => ({ o, i })));
                const audio = ch.type === "listen" ? `
                    <div class="sg-listen">
                        ${canSay(ch.audio, "_listen") ? `
                            <button type="button" class="sg-listen__play" data-say="${esc(ch.audio)}" data-who="_listen" aria-label="استمع">▶</button>
                            <button type="button" class="sg-btn sg-btn--sm sg-btn--ghost" data-say="${esc(ch.audio)}" data-who="_listen" data-slow="1">🐢 أبطأ</button>
                            <button type="button" class="sg-reveal" data-reveal="aud-${esc(ch.id)}">اعرض النص (تلميح)</button>`
                        : `<p class="sg-note">متصفحك لا يدعم النطق الآلي، لذلك نعرض النص مكتوبًا.</p>`}
                    </div>
                    <div class="sg-listen__text" id="aud-${esc(ch.id)}" ${canSay(ch.audio, "_listen") ? "hidden" : ""}>
                        <p>${de(ch.audio)}</p>${ch.audioAr ? `<p class="sg-tr">${ar(ch.audioAr)}</p>` : ""}
                    </div>` : "";
                inner = `${audio}
                    <p class="sg-challenge__prompt">${ar(ch.prompt)}</p>
                    <div class="sg-options">
                        ${opts.map(({ o, i }) => `<button type="button" class="sg-opt" data-opt="${i}">${o.de ? de(o.de) : `<span>${ar(o.ar)}</span>`}</button>`).join("")}
                    </div>`;
                break;
            }
            case "fill": {
                const [a, b] = fill(ch.sentence).split("___");
                inner = `
                    <p class="sg-challenge__prompt">أكمل الجملة:</p>
                    <p class="sg-fill" lang="de" dir="ltr">${esc(a)}<span class="sg-blank">＿＿＿</span>${esc(b)}</p>
                    ${ch.ar ? `<button type="button" class="sg-reveal" data-reveal="fl-${esc(ch.id)}">المعنى بالعربية</button><p class="sg-tr" id="fl-${esc(ch.id)}" hidden>${ar(ch.ar)}</p>` : ""}
                    <div class="sg-options sg-options--row">
                        ${shuffle(ch.options).map((o) => `<button type="button" class="sg-opt" data-fill="${esc(o)}">${de(o)}</button>`).join("")}
                    </div>`;
                break;
            }
            case "build": {
                run.ch.state.picked = [];
                inner = `
                    <p class="sg-challenge__prompt">${ar(ch.prompt)}</p>
                    <div class="sg-build__answer" lang="de" dir="ltr" aria-label="جملتك"><span class="sg-build__placeholder">اضغط على الكلمات بالترتيب…</span></div>
                    <div class="sg-build__bank" lang="de" dir="ltr">
                        ${shuffle(ch.tiles.map((t, i) => ({ t, i }))).map(({ t, i }) => `<button type="button" class="sg-tile" data-tile="${i}">${esc(fill(t))}</button>`).join("")}
                    </div>
                    <div class="sg-row">
                        <button type="button" class="sg-btn sg-btn--go" data-action="check-build">تحقّق</button>
                        <button type="button" class="sg-btn sg-btn--ghost" data-action="clear-build">مسح</button>
                    </div>`;
                break;
            }
            case "match": {
                run.ch.state = { left: null, right: null, done: 0 };
                const right = shuffle(ch.pairs.map((p, i) => ({ t: p[1], i })));
                inner = `
                    <p class="sg-challenge__prompt">${ar(ch.prompt)}</p>
                    <div class="sg-match">
                        <div class="sg-match__col" lang="de" dir="ltr">${ch.pairs.map((p, i) => `<button type="button" class="sg-tile" data-left="${i}">${esc(fill(p[0]))}</button>`).join("")}</div>
                        <div class="sg-match__col">${right.map(({ t, i }) => `<button type="button" class="sg-tile sg-tile--ar" data-right="${i}">${ar(t)}</button>`).join("")}</div>
                    </div>
                    <p class="sg-note">اختر كلمة ألمانية ثم معناها بالعربية.</p>`;
                break;
            }
            case "write": {
                inner = `
                    <p class="sg-challenge__prompt">${ar(ch.prompt)}</p>
                    <form class="sg-write" data-form="write" autocomplete="off">
                        <input name="answer" lang="de" dir="ltr" spellcheck="false" autocapitalize="sentences" placeholder="Schreib hier auf Deutsch …" aria-label="إجابتك بالألمانية">
                        <div class="sg-keys" aria-label="حروف ألمانية">${["ä", "ö", "ü", "ß", "Ä", "Ö", "Ü"].map((k) => `<button type="button" class="sg-key" data-key="${k}">${k}</button>`).join("")}</div>
                        <button class="sg-btn sg-btn--go" type="submit">تحقّق</button>
                    </form>
                    <p class="sg-note">لا تهتم بالحروف الكبيرة أو علامات الترقيم. يمكنك كتابة ae بدل ä و ss بدل ß.</p>`;
                break;
            }
            case "find": {
                run.ch.state.selected = new Set();
                inner = `
                    <p class="sg-challenge__prompt">${ar(ch.prompt)}</p>
                    ${ch.clue ? `<div class="sg-doc sg-doc--list"><div class="sg-doc__body" lang="de" dir="ltr">${fill(ch.clue).split("\n").map((l) => `<p>${esc(l)}</p>`).join("")}</div></div>` : ""}
                    <div class="sg-find">
                        ${ch.items.map((it, i) => `<button type="button" class="sg-find__item" data-find="${i}" aria-pressed="false">
                            <span class="sg-find__emoji" aria-hidden="true">${esc(it.emoji || "▫️")}</span>
                            ${de(it.de)}
                            ${it.ar ? `<small class="sg-find__ar" hidden>${ar(it.ar)}</small>` : ""}
                        </button>`).join("")}
                    </div>
                    <div class="sg-row">
                        <button type="button" class="sg-btn sg-btn--go" data-action="check-find">تحقّق من اختياري</button>
                        <button type="button" class="sg-reveal" data-action="find-ar">أظهر المعاني بالعربية (تلميح)</button>
                    </div>`;
                break;
            }
            default:
                inner = "";
        }
        run.ch.el = append(challengeFrame(ch, inner), "sg-item--challenge").querySelector(".sg-challenge");
        run.waiting = "challenge";
        if (ch.npc && data.sound) {
            say(ch.npc.de, ch.npc.who);
        } else if (ch.type === "listen" && data.sound) {
            setTimeout(() => run?.ch?.step === ch && say(ch.audio, "_listen"), 350);
        }
        $("input[name=answer]", run.ch.el)?.focus({ preventScroll: true });
    }

    function useHint(manual = true) {
        const c = run?.ch;
        if (!c || c.solved) {
            return;
        }
        if (manual && !c.hint) {
            c.hint = true;
            run.hintsUsed++;
        }
        const fb = $(".sg-feedback", c.el);
        if (!$(".sg-hintbox", fb)) {
            fb.insertAdjacentHTML("beforeend", `<div class="sg-hintbox">💡 ${ar(c.step.hint)}</div>`);
        }
        $("[data-action=hint]", c.el)?.setAttribute("disabled", "");
    }

    /** Wrong attempt: explain, hint, maybe cost a heart, offer the answer after a second miss. */
    function miss(whyHtml) {
        const c = run.ch;
        c.attempts++;
        run.sceneMistakes++;
        const fb = $(".sg-feedback", c.el);
        $(".sg-why", fb)?.remove();
        fb.insertAdjacentHTML("afterbegin", `<div class="sg-why">✗ ${whyHtml || "ليست الإجابة الصحيحة. حاول مرة أخرى."}</div>`);
        c.el.classList.remove("is-shake");
        void c.el.offsetWidth;
        c.el.classList.add("is-shake");
        if (c.attempts === 1) {
            run.hearts--;
            run.heartsLost++;
            updateHud();
            toast("💔 فقدت قلبًا — لا بأس، اقرأ الشرح وحاول مجددًا", "is-bad");
        }
        useHint(false);
        if (run.hearts <= 0) {
            c.solved = true; // no more answers while the "out of hearts" screen comes up
            setTimeout(failMission, 900);
            return;
        }
        if (c.attempts >= 2 && !$("[data-action=giveup]", c.el)) {
            $(".sg-challenge__tools", c.el).insertAdjacentHTML("beforeend",
                `<button type="button" class="sg-tool" data-action="giveup">👀 أظهر الحل (بدون نقاط)</button>`);
        }
    }

    /** Correct (or revealed) answer: points, explanation, NPC reply, continue. */
    function solve(revealedHtml = null) {
        const c = run.ch;
        const ch = c.step;
        c.solved = true;
        run.done++;
        run.sceneChallenges++;
        let gained = 0;
        if (revealedHtml) {
            run.revealed++;
        } else {
            const base = ch.final ? POINTS.final : ch.bonus ? POINTS.bonus : POINTS.base;
            gained = c.attempts === 0 ? base : Math.round(base / 2);
            if (c.attempts === 0) {
                run.firstTry++;
                if (!c.hint) {
                    gained += POINTS.noHint;
                }
                if (ch.final) {
                    run.finalFirstTry = true;
                }
            }
        }
        run.points += gained;
        c.el.classList.add(revealedHtml ? "is-revealed" : "is-solved");
        $$("button", c.el).forEach((b) => {
            if (!b.matches(".sg-say, .sg-reveal, [data-say]")) {
                b.disabled = true;
            }
        });
        $$("input", c.el).forEach((i) => { i.readOnly = true; });
        const fb = $(".sg-feedback", c.el);
        $(".sg-why", fb)?.remove();
        const praise = c.attempts === 0 ? (c.hint ? "صحيح! 👍" : "ممتاز! من أول محاولة 🎉") : "صحيح هذه المرة 👍";
        fb.insertAdjacentHTML("beforeend", `
            <div class="sg-explain ${revealedHtml ? "is-revealed" : ""}">
                <b>${revealedHtml ? "الإجابة الصحيحة:" : praise}</b>${revealedHtml ? ` ${revealedHtml}` : ""}
                <p>${ar(ch.explain)}</p>
                ${gained ? `<span class="sg-gain">+${gained}</span>` : ""}
            </div>`);
        if (gained) {
            toast(`+${gained} نقطة`, "is-good");
        }
        updateHud();
        if (ch.reply) {
            append(lineHtml(ch.reply));
            if (data.sound) {
                say(ch.reply.de, ch.reply.who);
            }
        }
        run.ch = null;
        continueButton();
        $(".sg-dock .sg-btn")?.focus({ preventScroll: true });
    }

    // choice / listen
    function pickOption(i) {
        const c = run.ch;
        const ch = c.step;
        const o = ch.options[i];
        const btn = $(`[data-opt="${i}"]`, c.el);
        if (o.correct) {
            btn.classList.add("is-right");
            const others = ch.options.filter((x, j) => x.correct && j !== i);
            const el = c.el;
            solve();
            if (others.length) {
                $(".sg-explain", el)?.insertAdjacentHTML("beforeend",
                    `<p class="sg-also">صحيح أيضًا: ${others.map((x) => (x.de ? de(x.de) : ar(x.ar))).join(" · ")}</p>`);
            }
        } else {
            btn.classList.add("is-wrong");
            btn.disabled = true;
            miss(ar(o.why));
        }
    }

    function revealChoice() {
        const ch = run.ch.step;
        const right = ch.options.map((o, i) => ({ o, i })).filter(({ o }) => o.correct);
        right.forEach(({ i }) => $(`[data-opt="${i}"]`, run.ch.el)?.classList.add("is-right"));
        solve(right.map(({ o }) => (o.de ? de(o.de) : ar(o.ar))).join(" · "));
    }

    // fill
    function pickFill(value) {
        const c = run.ch;
        const btn = $$("[data-fill]", c.el).find((b) => b.dataset.fill === value);
        if (c.step.answers.includes(value)) {
            btn.classList.add("is-right");
            $(".sg-blank", c.el).textContent = fill(value);
            $(".sg-blank", c.el).classList.add("is-filled");
            solve();
        } else {
            btn.classList.add("is-wrong");
            btn.disabled = true;
            miss(`«${de(value)}» لا يناسب هذه الجملة.`);
        }
    }

    // build
    function renderBuild() {
        const c = run.ch;
        const tiles = c.step.tiles;
        const box = $(".sg-build__answer", c.el);
        box.innerHTML = c.state.picked.length
            ? c.state.picked.map((ti, pos) => `<button type="button" class="sg-tile is-placed" data-placed="${pos}">${esc(fill(tiles[ti]))}</button>`).join("")
            : `<span class="sg-build__placeholder">اضغط على الكلمات بالترتيب…</span>`;
        $$("[data-tile]", c.el).forEach((b) => {
            b.classList.toggle("is-used", c.state.picked.includes(Number(b.dataset.tile)));
            b.disabled = c.solved || c.state.picked.includes(Number(b.dataset.tile));
        });
    }

    function checkBuild() {
        const c = run.ch;
        const words = c.state.picked.map((i) => fill(c.step.tiles[i]));
        if (!words.length) {
            toast("اختر الكلمات أولًا");
            return;
        }
        const said = words.join(" ").toLowerCase();
        const ok = c.step.answers.some((a) => a.map(fill).join(" ").toLowerCase() === said);
        if (ok) {
            $(".sg-build__answer", c.el).classList.add("is-right");
            renderBuild();
            solve();
        } else {
            const expectedLen = c.step.answers[0].length;
            const why = words.length !== expectedLen
                ? `عدد الكلمات غير صحيح: الجملة الصحيحة فيها ${expectedLen} قطع (مع علامة الترقيم).`
                : "الكلمات أو ترتيبها غير صحيح. تذكّر: الفعل في المكان الثاني في الجملة الخبرية، وفي أول السؤال بنعم/لا.";
            miss(why);
        }
    }

    function revealBuild() {
        const c = run.ch;
        const answer = c.step.answers[0];
        const pool = c.step.tiles.map((t, i) => ({ t, i }));
        c.state.picked = answer.map((w) => {
            const k = pool.findIndex((x) => x && x.t === w);
            const idx = pool[k].i;
            pool[k] = null;
            return idx;
        });
        renderBuild();
        solve(de(answer.map(fill).join(" ").replace(/\s([?.!,])/g, "$1")));
    }

    // match
    function pickMatch(side, i) {
        const c = run.ch;
        const st = c.state;
        const sel = side === "left" ? `[data-left="${i}"]` : `[data-right="${i}"]`;
        const btn = $(sel, c.el);
        if (btn.classList.contains("is-right")) {
            return;
        }
        $$(side === "left" ? "[data-left]" : "[data-right]", c.el).forEach((b) => b.classList.remove("is-picked"));
        st[side] = i;
        btn.classList.add("is-picked");
        if (st.left === null || st.right === null) {
            return;
        }
        const l = $(`[data-left="${st.left}"]`, c.el);
        const r = $(`[data-right="${st.right}"]`, c.el);
        if (st.left === st.right) {
            [l, r].forEach((b) => { b.classList.remove("is-picked"); b.classList.add("is-right"); b.disabled = true; });
            st.done++;
            if (st.done === c.step.pairs.length) {
                solve();
            }
        } else {
            [l, r].forEach((b) => { b.classList.remove("is-picked"); b.classList.add("is-wrong"); });
            setTimeout(() => [l, r].forEach((b) => b.classList.remove("is-wrong")), 600);
            const pair = c.step.pairs[st.left];
            miss(`«${de(pair[0])}» لا تعني «${ar(c.step.pairs[st.right][1])}».`);
        }
        st.left = null;
        st.right = null;
    }

    function revealMatch() {
        const c = run.ch;
        c.step.pairs.forEach((_, i) => {
            $(`[data-left="${i}"]`, c.el)?.classList.add("is-right");
            $(`[data-right="${i}"]`, c.el)?.classList.add("is-right");
        });
        solve(c.step.pairs.map((p) => `${de(p[0])} = ${ar(p[1])}`).join("، "));
    }

    // write
    function checkWrite(value) {
        const c = run.ch;
        const given = norm(value);
        if (!given) {
            toast("اكتب إجابتك أولًا");
            return;
        }
        const ok = c.step.answers.some((a) => norm(a) === given);
        if (ok) {
            $("input[name=answer]", c.el).classList.add("is-right");
            solve();
            const shown = c.step.answers[0];
            if (norm(shown) !== given) {
                $(".sg-explain", c.el)?.insertAdjacentHTML("beforeend", `<p class="sg-also">صيغة أخرى صحيحة: ${de(shown)}</p>`);
            }
        } else {
            // Close enough? Tell the learner which kind of slip it was.
            const best = c.step.answers.map(norm).find((a) => a.replace(/\s/g, "") === given.replace(/\s/g, ""));
            miss(best ? "قريب جدًا! انتبه للمسافات بين الكلمات." : "الجملة غير صحيحة بعد. راجع التلميح وحاول مرة أخرى.");
        }
    }

    // find
    function toggleFind(i) {
        const c = run.ch;
        const btn = $(`[data-find="${i}"]`, c.el);
        if (c.state.selected.has(i)) {
            c.state.selected.delete(i);
        } else {
            c.state.selected.add(i);
        }
        btn.classList.toggle("is-on", c.state.selected.has(i));
        btn.setAttribute("aria-pressed", String(c.state.selected.has(i)));
        btn.classList.remove("is-wrong");
    }

    function checkFind() {
        const c = run.ch;
        const targets = new Set(c.step.targets.map((t) => c.step.items.findIndex((it) => it.de === t)));
        const sel = c.state.selected;
        const extra = [...sel].filter((i) => !targets.has(i));
        const missing = [...targets].filter((i) => !sel.has(i));
        if (!extra.length && !missing.length) {
            $$("[data-find]", c.el).forEach((b) => b.classList.toggle("is-right", targets.has(Number(b.dataset.find))));
            solve();
            return;
        }
        extra.forEach((i) => $(`[data-find="${i}"]`, c.el).classList.add("is-wrong"));
        const parts = [];
        if (extra.length) {
            parts.push(`اخترت ${extra.length} ${extra.length === 1 ? "عنصرًا غير مطلوب" : "عناصر غير مطلوبة"} (باللون الأحمر)`);
        }
        if (missing.length) {
            parts.push(`ينقصك ${missing.length} ${missing.length === 1 ? "عنصر" : "عناصر"}`);
        }
        miss(`${parts.join("، و")}.`);
    }

    function revealFind() {
        const c = run.ch;
        const targets = c.step.targets;
        $$("[data-find]", c.el).forEach((b) => {
            const it = c.step.items[Number(b.dataset.find)];
            b.classList.remove("is-wrong", "is-on");
            b.classList.toggle("is-right", targets.includes(it.de));
        });
        solve(targets.map((t) => de(t)).join(" · "));
    }

    function giveUp() {
        const c = run?.ch;
        if (!c || c.solved) {
            return;
        }
        ({ choice: revealChoice, listen: revealChoice, build: revealBuild, match: revealMatch, find: revealFind,
           fill: () => solve(c.step.answers.map((a) => de(a)).join(" / ")),
           write: () => solve(de(c.step.answers[0])) })[c.step.type]();
    }

    // ---------- notebook drawer

    function toggleBook(force) {
        const book = $(".sg-book");
        if (!book || !run) {
            return;
        }
        const open = force ?? book.hidden;
        if (open) {
            const m = run.mission;
            book.innerHTML = `
                <div class="sg-book__head"><h2>📒 دفتر المهمة</h2><button type="button" class="sg-back" data-action="book">✕</button></div>
                <h3>العبارات المهمة</h3>
                <ul class="sg-exprs">${(m.expressions || []).map(exprHtml).join("")}</ul>
                <h3>المفردات</h3>
                <ul class="sg-vocab">${(m.vocab || []).map((v) => `<li>${de(v.de)}${speakBtn(v.de)}<span>${ar(v.ar)}</span></li>`).join("")}</ul>`;
        }
        book.hidden = !open;
        if (open) {
            $(".sg-book .sg-back")?.focus();
        }
    }

    const exprHtml = (e) => `
        <li class="sg-expr">
            <p class="sg-expr__de">${de(e.de)} ${speakBtn(e.de, "_")}</p>
            <p class="sg-expr__ar">${ar(e.ar)}</p>
            <p class="sg-expr__use">${ar(e.use)}</p>
            <p class="sg-expr__ex">${de(e.example)} ${speakBtn(e.example, "_")}<br><span>${ar(e.exampleAr)}</span></p>
        </li>`;

    // ---------- end of a mission

    function failMission() {
        if (!run) {
            return;
        }
        const m = run.mission;
        const p = data.progress[m.level][m.id] || (data.progress[m.level][m.id] = { done: false, best: 0, stars: 0, plays: 0 });
        p.plays++;
        save();
        const sc = m.scenes[run.sceneIndex];
        show("fail", `
            <div class="sg-card sg-end sg-end--fail">
                <p class="sg-end__icon" aria-hidden="true">💔</p>
                <h1>نفدت القلوب… لكن الرحلة لم تنتهِ!</h1>
                <p>كل خطأ درس. في ألمانيا الحقيقية أيضًا تقول: <b>${de("Entschuldigung, noch einmal bitte!")}</b> وتحاول من جديد.</p>
                <p class="sg-note">وصلت إلى المشهد ${run.sceneIndex + 1}: «${esc(sc.title.ar)}» وحللت ${run.done} من ${run.total} تحديًا.</p>
                <div class="sg-actions">
                    <button type="button" class="sg-btn sg-btn--go" data-action="retry-scene">أعد هذا المشهد بثلاثة قلوب ↺</button>
                    <button type="button" class="sg-btn" data-action="restart">ابدأ المهمة من أولها</button>
                    <button type="button" class="sg-btn sg-btn--ghost" data-action="map">الخريطة</button>
                </div>
                <p class="sg-note">ملاحظة: إعادة المشهد تُكمل المهمة، لكن النجمة الثالثة تحتاج إلى لعب المهمة كاملة دون أن تنفد القلوب.</p>
            </div>`);
    }

    function retryScene() {
        const m = run.mission;
        const snap = Object.assign({}, run.snapshot, { revived: true, prevBest: run.prevBest, hearts: 3, sceneIndex: run.sceneIndex });
        startMission(m, run.sceneIndex, snap);
    }

    function unlock(id, fresh) {
        if (!data.achievements[id]) {
            data.achievements[id] = Date.now();
            fresh.push(ACHIEVEMENTS.find((a) => a.id === id));
        }
    }

    function finishMission() {
        const m = run.mission;
        const level = m.level;
        const accuracy = run.done ? run.firstTry / run.done : 0;
        const crit = [
            { ok: true, text: "أكملت المهمة" },
            { ok: accuracy >= 0.7, text: `أجبت عن 70٪ على الأقل من التحديات من أول محاولة (نتيجتك ${Math.round(accuracy * 100)}٪)` },
            { ok: accuracy >= 0.9 && run.finalFirstTry && !run.revived,
              text: "دقة 90٪ أو أكثر، والتحدي النهائي صحيح من أول محاولة، دون أن تنفد القلوب" },
        ];
        const earnedStars = crit[1].ok ? (crit[2].ok ? 3 : 2) : 1;

        const bonuses = [];
        if (run.heartsLost === 0 && !run.revived) {
            bonuses.push(["مهمة بلا أخطاء (لم تفقد أي قلب)", POINTS.perfect]);
        }
        if (run.hintsUsed === 0 && run.revealed === 0) {
            bonuses.push(["بدون أي تلميح", POINTS.selfMade]);
        }
        const score = run.points + bonuses.reduce((s, b) => s + b[1], 0);
        const p = data.progress[level][m.id] || (data.progress[level][m.id] = { done: false, best: 0, stars: 0, plays: 0 });
        const improved = p.done && score > p.best;
        if (improved) {
            bonuses.push(["تحسّنت عن أفضل نتيجة سابقة", POINTS.improved]);
        }
        const total = score + (improved ? POINTS.improved : 0);

        const fresh = [];
        const firstTime = !p.done;
        p.plays++;
        p.done = true;
        p.best = Math.max(p.best, total);
        p.stars = Math.max(p.stars, earnedStars);
        data.points += total;

        unlock(m.id, fresh);
        if (run.heartsLost === 0 && !run.revived) {
            unlock("flawless", fresh);
        }
        if (run.hintsUsed === 0 && run.revealed === 0) {
            unlock("self", fresh);
        }
        if (improved) {
            unlock("comeback", fresh);
        }
        if (earnedStars === 3) {
            unlock("stars", fresh);
        }
        if (isDone("A1", m.id) && isDone("A2", m.id)) {
            unlock("both", fresh);
        }
        if (levelDone(level) === 7) {
            unlock(`journey${level}`, fresh);
        }
        save();

        const index = MISSIONS.findIndex((x) => x.id === m.id);
        const nextMission = MISSIONS[index + 1];
        const confetti = Array.from({ length: 28 }, (_, i) => `<i style="--x:${(i * 37) % 100};--d:${(i % 7) * 0.12}s;--c:${["var(--schwarz)", "var(--rot)", "var(--gold)"][i % 3]}"></i>`).join("");

        show("done", `
            <div class="sg-confetti" aria-hidden="true">${confetti}</div>
            <div class="sg-card sg-end">
                <p class="sg-kicker">المهمة ${index + 1} من 7 · ${level}</p>
                <h1>${firstTime ? "أنجزت المهمة!" : "أنجزت المهمة مجددًا!"} 🎉</h1>
                <p class="sg-end__stars">${stars(earnedStars)}</p>
                <ul class="sg-criteria">${crit.map((c, i) => `<li class="${c.ok ? "is-ok" : ""}"><span>${c.ok ? "★" : "☆"}</span> النجمة ${i + 1}: ${ar(c.text)}</li>`).join("")}</ul>
                <div class="sg-score">
                    <div><b>${total}</b><span>نقطة في هذه المهمة</span></div>
                    <div><b>${run.firstTry}/${run.done}</b><span>صحيحة من أول محاولة</span></div>
                    <div><b>${3 - Math.min(3, run.heartsLost)}</b><span>قلوب لم تفقدها</span></div>
                    <div><b>${run.hintsUsed}</b><span>تلميحات</span></div>
                </div>
                ${bonuses.length ? `<ul class="sg-bonuses">${bonuses.map(([t, n]) => `<li>${ar(t)} <b>+${n}</b></li>`).join("")}</ul>` : ""}
                ${fresh.length ? `<div class="sg-unlocked"><h2>إنجاز جديد!</h2><ul class="sg-badges">${fresh.map((a) => `
                    <li class="sg-badge is-on is-new"><span class="sg-badge__icon">${a.icon}</span><span class="sg-badge__name" lang="en" dir="ltr">${esc(a.name)}</span><span class="sg-badge__ar">${esc(a.ar)}</span></li>`).join("")}</ul></div>` : ""}
                <p class="sg-end__outro">${ar(m.outro)}</p>
            </div>
            <section class="sg-card sg-learned">
                <h2>ماذا تعلّمت؟</h2>
                <ul class="sg-summary">${(m.summary || []).map((s) => `<li>${ar(s)}</li>`).join("")}</ul>
                <details><summary>العبارات المهمة (${(m.expressions || []).length})</summary><ul class="sg-exprs">${(m.expressions || []).map(exprHtml).join("")}</ul></details>
                <details><summary>مفردات المهمة (${(m.vocab || []).length})</summary><ul class="sg-vocab">${(m.vocab || []).map((v) => `<li>${de(v.de)}${speakBtn(v.de)}<span>${ar(v.ar)}</span></li>`).join("")}</ul></details>
            </section>
            <div class="sg-actions">
                ${nextMission ? `<button type="button" class="sg-btn sg-btn--go" data-action="brief" data-mission="${nextMission.id}" data-autofocus>المهمة التالية: ${esc(nextMission.title[level][1])} ←</button>`
                    : `<p class="sg-final-note">🏁 أكملت رحلة ${level} كلها! ${level === "A1" ? "جرّب الآن مستوى A2 لحوارات أطول وأغنى." : "أنت الآن قادر على التعامل مع الحياة اليومية في ألمانيا. Herzlichen Glückwunsch!"}</p>`}
                <button type="button" class="sg-btn" data-action="restart">العب المهمة مجددًا ↺</button>
                <button type="button" class="sg-btn sg-btn--ghost" data-action="map">خريطة الرحلة</button>
            </div>`);
        run = null;
    }

    // ------------------------------------------------------------------ events

    root.addEventListener("click", (e) => {
        const t = e.target.closest("button, [data-action]");
        if (!t || !root.contains(t)) {
            return;
        }

        if (t.dataset.say !== undefined && t.dataset.say !== "") {
            if (!say(t.dataset.say, t.dataset.who || "_", t.dataset.slow === "1")) {
                toast("النطق غير متاح في هذا المتصفح");
            }
            return;
        }
        if (t.dataset.reveal) {
            const el = document.getElementById(t.dataset.reveal);
            if (el) {
                el.hidden = !el.hidden;
                t.classList.toggle("is-open", !el.hidden);
                // Revealing the text of a listening exercise counts as using a hint.
                if (!el.hidden && t.dataset.reveal.startsWith("aud-") && run?.ch && !run.ch.solved && !run.ch.hint) {
                    run.ch.hint = true;
                    run.hintsUsed++;
                }
            }
            return;
        }
        if (t.dataset.opt !== undefined && run?.ch && !run.ch.solved) {
            pickOption(Number(t.dataset.opt));
            return;
        }
        if (t.dataset.fill !== undefined && run?.ch && !run.ch.solved) {
            pickFill(t.dataset.fill);
            return;
        }
        if (t.dataset.tile !== undefined && run?.ch && !run.ch.solved) {
            run.ch.state.picked.push(Number(t.dataset.tile));
            renderBuild();
            return;
        }
        if (t.dataset.placed !== undefined && run?.ch && !run.ch.solved) {
            run.ch.state.picked.splice(Number(t.dataset.placed), 1);
            renderBuild();
            return;
        }
        if (t.dataset.left !== undefined && run?.ch && !run.ch.solved) {
            pickMatch("left", Number(t.dataset.left));
            return;
        }
        if (t.dataset.right !== undefined && run?.ch && !run.ch.solved) {
            pickMatch("right", Number(t.dataset.right));
            return;
        }
        if (t.dataset.find !== undefined && run?.ch && !run.ch.solved) {
            toggleFind(Number(t.dataset.find));
            return;
        }
        if (t.dataset.key) {
            const input = $("input[name=answer]", run?.ch?.el || root);
            if (input && !input.readOnly) {
                const s = input.selectionStart ?? input.value.length;
                input.value = input.value.slice(0, s) + t.dataset.key + input.value.slice(input.selectionEnd ?? s);
                input.focus();
                input.setSelectionRange(s + 1, s + 1);
            }
            return;
        }
        if (t.dataset.decide !== undefined && run?.waiting === "decision") {
            decide(Number(t.dataset.decide));
            return;
        }

        switch (t.dataset.action) {
            case "next":
                if (run?.waiting === "next") {
                    next();
                }
                break;
            case "hint":
                useHint(true);
                break;
            case "giveup":
                giveUp();
                break;
            case "check-build":
                if (run?.ch && !run.ch.solved) {
                    checkBuild();
                }
                break;
            case "clear-build":
                if (run?.ch && !run.ch.solved) {
                    run.ch.state.picked = [];
                    renderBuild();
                }
                break;
            case "check-find":
                if (run?.ch && !run.ch.solved) {
                    checkFind();
                }
                break;
            case "find-ar":
                if (run?.ch) {
                    $$(".sg-find__ar", run.ch.el).forEach((x) => { x.hidden = false; });
                    if (!run.ch.hint && !run.ch.solved) {
                        run.ch.hint = true;
                        run.hintsUsed++;
                    }
                    t.disabled = true;
                }
                break;
            case "book":
                toggleBook();
                break;
            case "map":
                run = null;
                renderMap();
                break;
            case "quit":
                if (window.confirm("هل تريد الخروج؟ سيضيع تقدّمك في هذه المهمة.")) {
                    run = null;
                    renderMap();
                }
                break;
            case "brief":
                openBriefing(t.dataset.mission);
                break;
            case "start":
                if (briefing) {
                    startMission(briefing);
                }
                break;
            case "restart": {
                const id = run?.mission?.id || briefing?.id;
                if (id) {
                    run = null;
                    loadMission(id, currentLevel()).then((m) => { briefing = m; startMission(m); });
                }
                break;
            }
            case "retry-scene":
                if (run) {
                    retryScene();
                }
                break;
            case "level":
                data.level = t.dataset.level;
                save();
                renderMap();
                break;
            case "profile":
                renderWelcome();
                break;
            case "sound":
                data.sound = !data.sound;
                save();
                renderMap();
                break;
            case "reset":
                if (window.confirm("سيُمسح كل تقدّمك ونقاطك وإنجازاتك في المستويين. هل أنت متأكد؟")) {
                    const profile = data.profile;
                    data = fresh();
                    data.profile = profile;
                    save();
                    renderMap();
                }
                break;
            default:
                break;
        }
    });

    root.addEventListener("change", (e) => {
        if (e.target.name === "level") {
            $$(".sg-levelpick").forEach((l) => l.classList.toggle("is-on", l.querySelector("input").checked));
        }
    });

    root.addEventListener("submit", (e) => {
        const form = e.target;
        e.preventDefault();
        if (form.dataset.form === "profile") {
            const fd = new FormData(form);
            const name = String(fd.get("name") || "").trim().replace(/\s+/g, " ").slice(0, 20);
            if (!name) {
                form.querySelector("[name=name]").focus();
                return;
            }
            const firstVisit = !data.profile;
            data.profile = { name, country: String(fd.get("country") || "Libyen") };
            data.level = LEVELS.includes(fd.get("level")) ? String(fd.get("level")) : "A1";
            save();
            if (firstVisit) {
                openBriefing("m1");
            } else {
                renderMap();
            }
        } else if (form.dataset.form === "write" && run?.ch && !run.ch.solved) {
            checkWrite(form.querySelector("[name=answer]").value);
        }
    });

    document.addEventListener("keydown", (e) => {
        if (screen !== "play" || !run) {
            return;
        }
        if (e.key === "Escape" && !$(".sg-book")?.hidden) {
            toggleBook(false);
            return;
        }
        const typing = e.target.matches?.("input, textarea, select");
        if ((e.key === "Enter" || e.key === " ") && !typing && run.waiting === "next" && !e.target.closest?.("button, a, summary")) {
            e.preventDefault();
            next();
        }
        if (run.waiting === "challenge" && !typing && /^[1-5]$/.test(e.key) && run.ch && ["choice", "listen", "fill"].includes(run.ch.step.type)) {
            const btns = $$(".sg-options .sg-opt", run.ch.el);
            const b = btns[Number(e.key) - 1];
            if (b && !b.disabled) {
                b.click();
            }
        }
    });

    window.addEventListener("beforeunload", (e) => {
        if (run && screen === "play") {
            e.preventDefault();
            e.returnValue = "";
        }
    });

    // ------------------------------------------------------------------ boot

    if (data.profile?.name) {
        renderMap();
    } else {
        renderWelcome();
    }
})();
