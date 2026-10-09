// Nelt — progressive enhancement only. Every page works without JavaScript.
(() => {
    "use strict";
    // Content is visible without JavaScript; entrance animations only hide it once this runs.
    document.documentElement.classList.add("js");

    const $ = (selector, root = document) => root.querySelector(selector);
    const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const csrf = () => $('meta[name="csrf-token"]')?.content ?? "";

    /* ---------- Header: scroll state + mobile menu ---------- */
    const header = $(".site-header");
    if (header) {
        const onScroll = () => header.classList.toggle("is-scrolled", window.scrollY > 8);
        onScroll();
        window.addEventListener("scroll", onScroll, { passive: true });
        $(".nav-toggle", header)?.addEventListener("click", (e) => {
            const open = header.classList.toggle("is-open");
            e.currentTarget.setAttribute("aria-expanded", String(open));
        });
    }

    const app = $(".app");
    if (app) {
        $$("[data-app-toggle]").forEach((btn) => btn.addEventListener("click", () => app.classList.toggle("is-open")));
        app.addEventListener("click", (e) => {
            if (app.classList.contains("is-open") && !e.target.closest(".app__side, [data-app-toggle]")) {
                app.classList.remove("is-open");
            }
        });
    }

    // Close language menus on outside click / Escape.
    document.addEventListener("click", (e) => {
        $$("details.lang[open]").forEach((d) => { if (!d.contains(e.target)) d.removeAttribute("open"); });
    });
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") $$("details.lang[open]").forEach((d) => d.removeAttribute("open"));
    });

    /* ---------- Reveal on scroll ---------- */
    const revealables = $$(".reveal, .reveal-up, .quote");
    if ("IntersectionObserver" in window && !reducedMotion) {
        const io = new IntersectionObserver((entries) => {
            entries.forEach((entry) => {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-visible");
                    io.unobserve(entry.target);
                }
            });
        }, { threshold: 0.18 });
        revealables.forEach((el) => io.observe(el));
    } else {
        revealables.forEach((el) => el.classList.add("is-visible"));
    }

    /* ---------- Zeitring: live Berlin time on three tricolour rings ---------- */
    const ring = $("[data-zeitring]");
    if (ring) {
        const zone = ring.dataset.zone || "Europe/Berlin";
        const fmt = new Intl.DateTimeFormat("en-GB", { timeZone: zone, hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" });
        const readout = $("[data-zeitring-time]", ring);
        const parts = {
            h: $('[data-ring="h"]', ring), m: $('[data-ring="m"]', ring), s: $('[data-ring="s"]', ring),
            hh: $('[data-hand="h"]', ring), mh: $('[data-hand="m"]', ring), sh: $('[data-hand="s"]', ring),
        };
        const length = (el) => Number(el.dataset.len);
        const setRing = (el, fraction) => { el.style.strokeDashoffset = String(length(el) * (1 - fraction)); };
        const setHand = (el, degrees) => { el.style.transform = `rotate(${degrees}deg)`; };

        const tick = () => {
            const [h, m, s] = fmt.format(new Date()).split(":").map(Number);
            const ms = new Date().getMilliseconds();
            const seconds = s + (reducedMotion ? 0 : ms / 1000);
            setRing(parts.s, seconds / 60);
            setRing(parts.m, (m + seconds / 60) / 60);
            setRing(parts.h, ((h % 12) + m / 60) / 12);
            setHand(parts.sh, seconds * 6);
            setHand(parts.mh, (m + seconds / 60) * 6);
            setHand(parts.hh, ((h % 12) + m / 60) * 30);
            if (readout) readout.textContent = `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
        };

        tick();
        if (reducedMotion) {
            setInterval(tick, 15000);
        } else {
            // Let the draw-in animation finish before the rings start following the clock.
            setTimeout(() => { $$(".zeitring__draw", ring).forEach((el) => el.classList.remove("zeitring__draw")); tick(); setInterval(tick, 250); }, 1600);
        }
    }

    /* ---------- Multilingual field tabs ---------- */
    $$("[data-lt]").forEach((field) => {
        const tabs = $$("[data-lt-tab]", field);
        const panes = $$("[data-lt-pane]", field);
        tabs.forEach((tab) => tab.addEventListener("click", () => {
            const key = tab.dataset.ltTab;
            tabs.forEach((t) => { const on = t === tab; t.classList.toggle("is-active", on); t.setAttribute("aria-selected", String(on)); });
            panes.forEach((p) => { p.hidden = p.dataset.ltPane !== key; });
            panes.find((p) => !p.hidden)?.focus();
        }));
        panes.forEach((pane) => pane.addEventListener("input", () => {
            tabs.find((t) => t.dataset.ltTab === pane.dataset.ltPane)?.classList.toggle("has-value", pane.value.trim() !== "");
        }));
        // If the server flagged an error, reveal English (the required language).
    });

    /* ---------- Confirmations ---------- */
    document.addEventListener("submit", (e) => {
        const form = e.target;
        const message = form.dataset.confirm;
        if (message && !window.confirm(message)) {
            e.preventDefault();
            return;
        }
        // Prevent double submits on slow uploads.
        const button = e.submitter;
        if (button && !form.dataset.noLock) {
            setTimeout(() => { button.disabled = true; button.classList.add("is-disabled"); }, 0);
        }
    });

    /* ---------- Flash messages ---------- */
    $$(".flash").forEach((flash) => {
        const close = () => { flash.classList.add("is-leaving"); setTimeout(() => flash.remove(), 300); };
        $(".flash__close", flash)?.addEventListener("click", close);
        if (!flash.classList.contains("flash--error")) setTimeout(close, 6000);
    });

    /* ---------- Quiz timer ---------- */
    const quizForm = $("[data-quiz]");
    if (quizForm) {
        let dirty = false;
        let submitting = false;
        quizForm.addEventListener("change", () => { dirty = true; });
        quizForm.addEventListener("submit", () => { submitting = true; });
        window.addEventListener("beforeunload", (e) => { if (dirty && !submitting) { e.preventDefault(); e.returnValue = ""; } });

        const deadline = Number(quizForm.dataset.deadline || 0);
        const timer = $("[data-timer]");
        if (deadline && timer) {
            const update = () => {
                const left = Math.max(0, Math.floor((deadline - Date.now()) / 1000));
                const mm = String(Math.floor(left / 60)).padStart(2, "0");
                const ss = String(left % 60).padStart(2, "0");
                timer.querySelector("span").textContent = `${mm}:${ss}`;
                timer.classList.toggle("is-low", left <= 60);
                if (left === 0 && !submitting) {
                    submitting = true;
                    quizForm.requestSubmit();
                }
            };
            update();
            setInterval(update, 1000);
        }
    }

    /* ---------- Question editor ---------- */
    const editor = $("[data-question-editor]");
    if (editor) {
        const typeSelect = $("[data-question-type]", editor);
        const choice = $("[data-choice-options]", editor);
        const trueFalse = $("[data-truefalse]", editor);
        const rows = $("[data-option-rows]", editor);
        const template = $("[data-option-template]", editor);

        const renumber = () => {
            $$(".opt-row", rows).forEach((row, i) => {
                $$("input", row).forEach((input) => { input.name = input.name.replace(/Options\[\d+\]/, `Options[${i}]`); });
            });
        };
        const syncType = () => {
            const tf = typeSelect.value === "TrueFalse";
            choice.hidden = tf;
            trueFalse.hidden = !tf;
            $$("input", choice).forEach((i) => { i.disabled = tf; });
            $$("input", trueFalse).forEach((i) => { i.disabled = !tf; });
            const single = typeSelect.value === "SingleChoice";
            $$('input[type="checkbox"][data-correct]', rows).forEach((box) => { box.dataset.single = single ? "1" : ""; });
        };
        rows.addEventListener("change", (e) => {
            const box = e.target.closest("[data-correct]");
            if (box && box.checked && box.dataset.single) {
                $$("[data-correct]", rows).forEach((other) => { if (other !== box) other.checked = false; });
            }
        });
        editor.addEventListener("click", (e) => {
            if (e.target.closest("[data-add-option]")) {
                const index = $$(".opt-row", rows).length;
                const row = template.content.firstElementChild.cloneNode(true);
                $$("input", row).forEach((input) => { input.name = input.name.replace("__i__", String(index)); });
                rows.appendChild(row);
                syncType();
                $('input[type="text"]', row)?.focus();
            }
            const remove = e.target.closest("[data-remove-option]");
            if (remove && $$(".opt-row", rows).length > 2) {
                remove.closest(".opt-row").remove();
                renumber();
            }
        });
        typeSelect.addEventListener("change", syncType);
        syncType();
    }

    /* ---------- Lesson player: mark complete when the video ends ---------- */
    const player = $("[data-lesson-player]");
    if (player) {
        const video = $("video", player);
        const url = player.dataset.completeUrl;
        const doneBadge = $("[data-lesson-done]");
        let sent = player.dataset.completed === "true";
        if (video && url) {
            video.addEventListener("ended", async () => {
                if (sent) return;
                sent = true;
                try {
                    const response = await fetch(url, { method: "POST", headers: { "Accept": "application/json", "RequestVerificationToken": csrf() }, credentials: "same-origin" });
                    if (response.ok && doneBadge) doneBadge.hidden = false;
                    $(`[data-lesson-tick="${player.dataset.lessonId}"]`)?.classList.add("is-done");
                } catch { sent = false; }
            });
        }
    }

    /* ---------- Attendance sheet helper ---------- */
    $$("[data-mark-all]").forEach((btn) => btn.addEventListener("click", () => {
        const value = btn.dataset.markAll;
        $$(`input[type="radio"][value="${value}"]`, btn.closest("form")).forEach((radio) => {
            const group = radio.name;
            const anyChecked = $$(`input[name="${CSS.escape(group)}"]:checked`).some((r) => r.value !== "");
            if (!anyChecked) radio.checked = true;
        });
    }));

    /* ---------- Copy to clipboard ---------- */
    $$("[data-copy]").forEach((btn) => btn.addEventListener("click", async () => {
        const target = document.getElementById(btn.dataset.copy);
        if (!target) return;
        try {
            await navigator.clipboard.writeText(target.textContent.trim());
            const original = btn.innerHTML;
            btn.textContent = btn.dataset.copied || "✓";
            setTimeout(() => { btn.innerHTML = original; }, 1500);
        } catch { /* clipboard unavailable: text stays selectable */ }
    }));

    /* ---------- Print buttons (no inline handlers: CSP forbids them) ---------- */
    $$("[data-print]").forEach((btn) => btn.addEventListener("click", () => window.print()));

    /* ---------- Auto-submit filters ---------- */
    $$("[data-autosubmit]").forEach((el) => el.addEventListener("change", () => el.form?.requestSubmit()));
})();
