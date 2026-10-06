(function () {
    'use strict';

    // Plugin Pages appends the page fragment on every visit, so this script can run more
    // than once in the same web client
    if (window.McuTimeline) {
        window.McuTimeline.mountPending();
        return;
    }

    var assets = document.currentScript.src.replace(/[^/]*$/, '');
    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    var phone = window.matchMedia('(max-width: 639px)');

    // registered so the accent can transition, @property is ignored inside a shadow root
    try {
        CSS.registerProperty({ name: '--mcu-accent', syntax: '<color>', inherits: true, initialValue: '#e23636' });
    } catch (e) {
        // already registered, or not supported: the accent then switches without fading
    }

    function svg(path) {
        return '<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="' + path + '"/></svg>';
    }

    var ICONS = {
        play: svg('M8 5v14l11-7z'),
        check: svg('M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z'),
        missing: svg('M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 0 1-6.3-12.9l11.2 11.2A8 8 0 0 1 12 20zm6.3-3.1L7.1 5.7A8 8 0 0 1 18.3 16.9z'),
        clock: svg('M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16zm.5-13H11v6l5.2 3.2.8-1.3-4.5-2.7z'),
        hourglass: svg('M6 2v6l4 4-4 4v6h12v-6l-4-4 4-4V2zm10 14.5V20H8v-3.5l4-4zm-4-5-4-4V4h8v3.5z'),
        info: svg('M11 7h2v2h-2zm0 4h2v6h-2zm1-9a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16z'),
        send: svg('M2 21 23 12 2 3v7l15 2-15 2z'),
        left: svg('M15.4 7.4 14 6l-6 6 6 6 1.4-1.4-4.6-4.6z'),
        right: svg('M8.6 16.6 10 18l6-6-6-6-1.4 1.4 4.6 4.6z'),
        close: svg('M19 6.4 17.6 5 12 10.6 6.4 5 5 6.4 10.6 12 5 17.6 6.4 19 12 13.4 17.6 19 19 17.6 13.4 12z')
    };

    var S = null;
    var css = null;
    var ready = null;
    var readyLanguage = null;

    // the web client puts its display language on <html lang>
    function language() {
        return document.documentElement.lang || navigator.language || 'en';
    }

    function stringsFile() {
        return /^fr\b/i.test(language()) ? 'strings-fr.json' : 'strings-en.json';
    }

    function client() {
        return window.ApiClient && window.ApiClient.accessToken() ? window.ApiClient : null;
    }

    function authFetch(url, method) {
        return fetch(url, {
            method: method || 'GET',
            headers: { Authorization: 'MediaBrowser Token="' + client().accessToken() + '"' }
        });
    }

    function fetchOk(url) {
        return authFetch(url).then(function (response) {
            if (!response.ok) {
                throw new Error(url + ': HTTP ' + response.status);
            }
            return response;
        });
    }

    function loadAssets() {
        // a language change in the user settings takes effect on the next visit
        if (!ready || readyLanguage !== stringsFile()) {
            readyLanguage = stringsFile();
            ready = Promise.all([
                fetchOk(assets + readyLanguage).then(function (r) { return r.json(); }),
                fetchOk(assets + 'timeline.css').then(function (r) { return r.text(); })
            ]).then(function (results) {
                S = results[0];
                css = results[1];
            });
            ready.catch(function () {
                ready = null;
            });
        }
        return ready;
    }

    // --- helpers ---

    function format(template, values) {
        return template.replace(/\{(\w+)\}/g, function (match, key) {
            return values[key] !== undefined ? values[key] : match;
        });
    }

    function escapeHtml(text) {
        return String(text == null ? '' : text)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function longDate(iso) {
        var parts = iso.split('-');
        var date = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
        return new Intl.DateTimeFormat(S.locale, { day: 'numeric', month: 'short', year: 'numeric' }).format(date);
    }

    function runtime(ticks) {
        var minutes = Math.round(ticks / 600000000);
        var h = Math.floor(minutes / 60);
        var m = minutes % 60;
        return h ? format(S.runtimeHours, { h: h, m: (m < 10 ? '0' : '') + m }) : format(S.runtimeMinutes, { m: m });
    }

    // Jellyfin serialises PascalCase
    function camelize(value) {
        if (Array.isArray(value)) {
            return value.map(camelize);
        }
        if (value && typeof value === 'object') {
            var out = {};
            Object.keys(value).forEach(function (key) {
                out[key.charAt(0).toLowerCase() + key.slice(1)] = camelize(value[key]);
            });
            return out;
        }
        return value;
    }

    function tmdbSize(url, size) {
        return url.replace(/\/t\/p\/[^/]+\//, '/t/p/' + size + '/');
    }

    function scrollBehavior() {
        return reduceMotion.matches ? 'auto' : 'smooth';
    }

    // --- toast, under body: the page's containment would pin a fixed child to the page ---

    var toastRoot = null;
    var toastTimer = null;

    function toast(text) {
        if (!toastRoot) {
            var host = document.createElement('div');
            document.body.appendChild(host);
            toastRoot = host.attachShadow({ mode: 'open' });
            toastRoot.innerHTML = '<style>' + css + '</style><div class="toast" role="status" aria-live="polite"></div>';
        }
        var el = toastRoot.querySelector('.toast');
        el.textContent = text;
        el.classList.add('visible');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { el.classList.remove('visible'); }, 3500);
    }

    // --- one timeline per page ---

    function mount(host) {
        host.setAttribute('data-mcu-mounted', '');
        var root = host.attachShadow({ mode: 'open' });
        var $ = function (selector) { return root.querySelector(selector); };

        var api = null;
        var data = null;
        var byId = {};
        var cards = {};
        var visible = [];
        var posters = {};
        var requests = {};
        var selectedId = null;
        var nextId = null;
        var prefs = { order: 'release', types: [], phases: [], onlyOwned: false };
        var revealObserver = null;
        var revealQueue = [];
        var revealing = false;
        var backdropLayer = 0;
        var heroToken = 0;

        function setHeaderOffset() {
            var header = document.querySelector('.skinHeader');
            host.style.setProperty('--header', (header ? header.offsetHeight : 0) + 'px');
        }

        function setPageTitle() {
            var title = document.querySelector('.skinHeader .pageTitle');
            if (title) {
                title.innerText = S.pageTitle;
            }
            document.title = S.pageTitle;
        }

        function showMessage(text, retry) {
            root.innerHTML = '<style>' + css + '</style><div class="mcu-timeline"><p class="message" role="status"></p></div>';
            var message = $('.message');
            message.textContent = text;
            if (retry) {
                var button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn';
                button.textContent = S.retry;
                button.addEventListener('click', load);
                message.appendChild(document.createElement('br'));
                message.appendChild(button);
            }
        }

        // --- preferences, per user in this browser ---

        function prefsKey() {
            return 'mcuTimeline.prefs.v2.' + data.userId;
        }

        function loadPrefs() {
            try {
                var saved = JSON.parse(localStorage.getItem(prefsKey()) || '{}');
                if (saved.order === 'release' || saved.order === 'chrono') {
                    prefs.order = saved.order;
                }
                prefs.types = Array.isArray(saved.types) ? saved.types.filter(function (t) { return S.types[t]; }) : [];
                prefs.phases = Array.isArray(saved.phases) ? saved.phases.filter(function (p) { return p >= 1 && p <= 6; }) : [];
                prefs.onlyOwned = saved.onlyOwned === true;
            } catch (e) {
                // defaults
            }
        }

        function savePrefs() {
            try {
                localStorage.setItem(prefsKey(), JSON.stringify(prefs));
            } catch (e) {
                // private mode, choices last for this visit only
            }
        }

        // --- item facts ---

        function stateOf(item) {
            if (item.status === 'owned') {
                return item.played ? 'played' : 'owned';
            }
            return item.status;
        }

        function stateLabel(item) {
            if (item.status === 'owned') {
                return item.played ? S.played : item.inProgress ? format(S.inProgress, { percent: Math.round(item.progress * 100) }) : S.statusOwned;
            }
            if (requests[item.id]) {
                return S.requestStatus[requests[item.id]];
            }
            return item.status === 'upcoming' ? format(S.upcomingOn, { date: longDate(item.releaseDate) }) : S.statusAbsent;
        }

        function posterUrl(item, width) {
            if (item.status === 'owned') {
                return client().getImageUrl(item.jellyfinId, { type: 'Primary', maxWidth: width, quality: 90 });
            }
            return posters[item.id] ? tmdbSize(posters[item.id], width > 400 ? 'w780' : 'w342') : null;
        }

        function rank(item) {
            return prefs.order === 'release' ? item.releaseRank : item.chronoRank;
        }

        function groupOf(item) {
            return prefs.order === 'release' ? item.saga + '/' + item.phase : item.era;
        }

        function yearsOf(items) {
            var years = items.map(function (item) { return Number(item.releaseDate.slice(0, 4)); });
            var from = Math.min.apply(null, years);
            var to = Math.max.apply(null, years);
            return from === to ? String(from) : format(S.years, { from: from, to: to });
        }

        // a group gets its own slot in the rail, so its name never runs over the next one
        function createMarker(item) {
            var kicker;
            var title;
            var sub;
            if (prefs.order === 'release') {
                kicker = S.sagas[item.saga] || item.saga;
                title = format(S.phase, { n: item.phase });
                sub = yearsOf(data.items.filter(function (other) { return other.phase === item.phase; }));
            } else {
                var era = S.eras[item.era] || { name: item.era, range: '' };
                kicker = S.eraLabel;
                title = era.name;
                sub = era.range;
            }
            var li = document.createElement('li');
            li.className = 'marker';
            li.setAttribute('aria-hidden', 'true');
            li.innerHTML = '<span class="marker-box"><span class="marker-kicker">' + escapeHtml(kicker) + '</span>'
                + '<span class="marker-title">' + escapeHtml(title) + '</span>'
                + (sub ? '<span class="marker-sub">' + escapeHtml(sub) + '</span>' : '') + '</span>'
                + '<span class="axis"><span class="link"></span><span class="tick"></span></span>';
            return li;
        }

        // --- cards ---

        function badgeHtml(item) {
            var state = stateOf(item);
            var icon = null;
            if (state === 'played') {
                icon = ICONS.check;
            } else if (state === 'upcoming') {
                icon = requests[item.id] ? ICONS.hourglass : ICONS.clock;
            } else if (state === 'absent') {
                icon = requests[item.id] ? ICONS.hourglass : ICONS.missing;
            }
            return icon ? '<span class="badge" title="' + escapeHtml(stateLabel(item)) + '">' + icon + '</span>' : '';
        }

        function cardLabel(item) {
            return [item.title, item.releaseDate.slice(0, 4), format(S.phase, { n: item.phase }), S.typeSingular[item.type], stateLabel(item)]
                .join(', ');
        }

        function fillCard(li, item) {
            var url = posterUrl(item, 320);
            li.className = 'card state-' + stateOf(item) + (item.inProgress ? ' in-progress' : '')
                + (li.classList.contains('pending') ? ' pending' : '');
            li.style.setProperty('--card-accent', item.accentColor || '#e23636');
            li.innerHTML = '<button type="button" class="card-hit" tabindex="-1" aria-label="' + escapeHtml(cardLabel(item)) + '">'
                + '<span class="poster"><span class="poster-frame">'
                + (url ? '<img alt="" loading="lazy" decoding="async" src="' + escapeHtml(url) + '">' : '')
                + '<span class="fallback-title"' + (url ? ' hidden' : '') + '>' + escapeHtml(item.title) + '</span>'
                + badgeHtml(item)
                + (item.inProgress ? '<span class="progress"><span style="transform:scaleX(' + item.progress.toFixed(3) + ')"></span></span>' : '')
                + (item.status === 'upcoming' ? '<span class="date-tag">' + escapeHtml(longDate(item.releaseDate)) + '</span>' : '')
                + '</span></span></button>'
                + '<span class="axis" aria-hidden="true"><span class="link"></span><span class="dot"></span></span>'
                + '<span class="card-text" aria-hidden="true"><span class="card-title">' + escapeHtml(item.title) + '</span>'
                + '<span class="card-sub"><span>' + item.releaseDate.slice(0, 4) + '</span></span></span>';
        }

        function createCards() {
            cards = {};
            data.items.forEach(function (item) {
                var li = document.createElement('li');
                li.dataset.id = item.id;
                li.classList.add('pending');
                fillCard(li, item);
                cards[item.id] = li;
            });
        }

        function refreshCard(item) {
            var li = cards[item.id];
            var wasSelected = li.classList.contains('selected');
            fillCard(li, item);
            li.classList.toggle('selected', wasSelected);
            li.querySelector('.card-hit').tabIndex = wasSelected ? 0 : -1;
            markNext();
            updateLinks();
        }

        function onImageEvent(event) {
            var img = event.target;
            if (img.tagName !== 'IMG') {
                return;
            }
            if (event.type === 'load') {
                img.classList.add('loaded');
            } else {
                img.hidden = true;
                var fallback = img.parentNode.querySelector('.fallback-title');
                if (fallback) {
                    fallback.hidden = false;
                }
            }
        }

        // --- reveal on scroll: 8 at a time, 30 ms apart ---

        function observeReveal(li) {
            if (!revealObserver || !li.classList.contains('pending')) {
                return;
            }
            revealObserver.observe(li);
        }

        function flushReveal() {
            if (revealing) {
                return;
            }
            // a fast scroll would queue a long wait, past 16 the rest shows at once
            if (revealQueue.length > 16) {
                revealQueue.splice(8).forEach(function (el) { el.classList.remove('pending'); });
            }
            var batch = revealQueue.splice(0, 8);
            if (!batch.length) {
                return;
            }
            revealing = true;
            requestAnimationFrame(function () {
                batch.forEach(function (el, i) {
                    el.classList.add('revealing');
                    el.style.transitionDelay = (i * 30) + 'ms';
                    el.classList.remove('pending');
                });
                setTimeout(function () {
                    batch.forEach(function (el) {
                        el.classList.remove('revealing');
                        el.style.transitionDelay = '';
                    });
                    revealing = false;
                    flushReveal();
                }, 8 * 30 + 320);
            });
        }

        function setupReveal() {
            if (reduceMotion.matches || !('IntersectionObserver' in window)) {
                Object.keys(cards).forEach(function (id) { cards[id].classList.remove('pending'); });
                return;
            }
            revealObserver = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        revealObserver.unobserve(entry.target);
                        revealQueue.push(entry.target);
                    }
                });
                flushReveal();
            }, { threshold: .1 });
        }

        // --- layout: filter, order, groups ---

        function computeVisible() {
            return data.items.filter(function (item) {
                if (prefs.types.length && prefs.types.indexOf(item.type) < 0) {
                    return false;
                }
                if (prefs.phases.length && prefs.phases.indexOf(item.phase) < 0) {
                    return false;
                }
                return !(prefs.onlyOwned && item.status !== 'owned');
            }).sort(function (a, b) { return rank(a) - rank(b); });
        }

        function markNext() {
            var next = null;
            for (var i = 0; i < visible.length; i++) {
                if (visible[i].status === 'owned' && !visible[i].played) {
                    next = visible[i];
                    break;
                }
            }
            nextId = next ? next.id : null;
            Object.keys(cards).forEach(function (id) {
                var sub = cards[id].querySelector('.card-sub');
                var tag = sub.querySelector('.next-tag');
                if (id === nextId && !tag) {
                    sub.insertAdjacentHTML('beforeend', '<span class="next-tag">' + escapeHtml(S.nextBadge) + '</span>');
                } else if (id !== nextId && tag) {
                    tag.remove();
                }
            });
        }

        // greens the axis between two watched titles that follow each other, markers in
        // between included
        function updateLinks() {
            var items = Array.prototype.slice.call($('.rail').children);
            var played = function (li) { return li.classList.contains('state-played'); };
            items.forEach(function (li) { li.classList.remove('done-in', 'done-out'); });
            var last = null;
            items.forEach(function (li, index) {
                if (!li.classList.contains('card')) {
                    return;
                }
                if (last !== null && played(items[last]) && played(li)) {
                    items[last].classList.add('done-out');
                    for (var k = last + 1; k < index; k++) {
                        items[k].classList.add('done-in', 'done-out');
                    }
                    li.classList.add('done-in');
                }
                last = index;
            });
        }

        function positions() {
            var out = {};
            visible.forEach(function (item) {
                var li = cards[item.id];
                if (li.isConnected) {
                    var rect = li.getBoundingClientRect();
                    out[item.id] = { x: rect.left, y: rect.top };
                }
            });
            return out;
        }

        function layout(animate) {
            var rail = $('.rail');
            var before = animate && !reduceMotion.matches ? positions() : null;
            var anchor = selectedId && cards[selectedId].isConnected ? cards[selectedId].getBoundingClientRect() : null;

            visible = computeVisible();
            var fragment = document.createDocumentFragment();
            var previous = null;
            visible.forEach(function (item) {
                if (!previous || groupOf(previous) !== groupOf(item)) {
                    fragment.appendChild(createMarker(item));
                }
                fragment.appendChild(cards[item.id]);
                previous = item;
            });
            rail.textContent = '';
            rail.appendChild(fragment);
            visible.forEach(function (item) { observeReveal(cards[item.id]); });

            $('.message').textContent = visible.length ? '' : S.empty;
            markNext();
            updateLinks();
            renderMinimapTrack();
            updateCounter();

            if (!visible.some(function (item) { return item.id === selectedId; })) {
                select(nextId || (visible[0] && visible[0].id), { scroll: true, instant: true });
            } else if (anchor) {
                // the selected title stays where the eye is, the rest moves around it
                var now = cards[selectedId].getBoundingClientRect();
                if (phone.matches) {
                    window.scrollBy(0, now.top - anchor.top);
                } else {
                    rail.scrollLeft += now.left - anchor.left;
                }
            }

            if (before) {
                flip(before);
            }
            updateMinimapWindow();
        }

        // FLIP: each poster slides from its old place to its new one
        function flip(before) {
            var vw = window.innerWidth;
            var vh = window.innerHeight;
            var moved = [];
            visible.forEach(function (item) {
                var li = cards[item.id];
                var old = before[item.id];
                if (!old || li.classList.contains('pending')) {
                    return;
                }
                var rect = li.getBoundingClientRect();
                var dx = old.x - rect.left;
                var dy = old.y - rect.top;
                var onScreen = rect.right > -vw && rect.left < vw * 2 && rect.bottom > -vh && rect.top < vh * 2;
                if ((dx || dy) && onScreen) {
                    li.style.transform = 'translate(' + dx + 'px,' + dy + 'px)';
                    moved.push(li);
                }
            });
            if (!moved.length) {
                return;
            }
            requestAnimationFrame(function () {
                moved.forEach(function (li) {
                    li.classList.add('moving');
                    li.style.transform = '';
                });
                setTimeout(function () {
                    moved.forEach(function (li) { li.classList.remove('moving'); });
                }, 650);
            });
        }

        function updateCounter() {
            var owned = visible.filter(function (item) { return item.status === 'owned'; });
            var seen = owned.filter(function (item) { return item.played; }).length;
            $('.counter').textContent = format(S.counter, { seen: seen, total: owned.length });
        }

        // --- selection and hero ---

        function select(id, options) {
            options = options || {};
            if (!id) {
                selectedId = null;
                renderHero(null);
                return;
            }
            if (selectedId && cards[selectedId]) {
                cards[selectedId].classList.remove('selected');
                cards[selectedId].querySelector('.card-hit').tabIndex = -1;
            }
            selectedId = id;
            var li = cards[id];
            li.classList.add('selected');
            var hit = li.querySelector('.card-hit');
            hit.tabIndex = 0;
            if (options.focus) {
                hit.focus({ preventScroll: true });
            }
            if (options.scroll) {
                li.scrollIntoView({ inline: 'center', block: 'nearest', behavior: options.instant ? 'auto' : scrollBehavior() });
            }
            root.querySelector('.mcu-timeline').style.setProperty('--mcu-accent', byId[id].accentColor || '#e23636');
            Array.prototype.forEach.call(root.querySelectorAll('.minimap-track span'), function (span) {
                span.classList.toggle('selected', span.dataset.id === id);
            });
            renderHero(byId[id]);
        }

        function setBackdrop(item) {
            var urls = [];
            if (item.status === 'owned') {
                urls.push({ url: client().getImageUrl(item.jellyfinId, { type: 'Backdrop', maxWidth: 1600, quality: 85 }), soft: false });
            }
            // a portrait poster stretched to the hero width needs some blur to pass as a backdrop
            var poster = posterUrl(item, 780);
            if (poster) {
                urls.push({ url: poster, soft: true });
            }
            var token = heroToken;

            (function tryNext() {
                var next = urls.shift();
                if (!next) {
                    return;
                }
                var url = next.url;
                var img = new Image();
                img.onload = function () {
                    if (token !== heroToken) {
                        return;
                    }
                    var layers = root.querySelectorAll('.hero-bg-layer');
                    backdropLayer = 1 - backdropLayer;
                    layers[backdropLayer].style.backgroundImage = 'url("' + url.replace(/"/g, '%22') + '")';
                    layers[backdropLayer].classList.toggle('soft', next.soft);
                    layers[backdropLayer].classList.add('visible');
                    layers[1 - backdropLayer].classList.remove('visible');
                };
                img.onerror = tryNext;
                img.src = url;
            })();
        }

        function heroHtml(item) {
            var owned = item.status === 'owned';
            var eyebrow = item.id === nextId ? S.upNext : (S.sagas[item.saga] || item.saga) + ' · ' + format(S.phase, { n: item.phase });

            var meta = [item.status === 'upcoming' ? longDate(item.releaseDate) : item.releaseDate.slice(0, 4), S.typeSingular[item.type]];
            if (owned && item.type === 'series') {
                meta.push(item.episodeCount === 1 ? S.episode : format(S.episodes, { count: item.episodeCount }));
            } else if (owned && item.runTimeTicks) {
                meta.push(runtime(item.runTimeTicks));
            }
            if (item.type === 'series' && item.seasons.length) {
                meta.push(format(S.seasons, { list: item.seasons.join(', ') }));
            }
            if (item.storyYear) {
                meta.push(format(S.storyYear, { year: item.storyYear }));
            }

            var stateIcon = item.played ? ICONS.check
                : owned ? ICONS.play
                : requests[item.id] ? ICONS.hourglass
                : item.status === 'upcoming' ? ICONS.clock : ICONS.missing;
            var state = '<p class="state-pill">' + stateIcon + '<span>' + escapeHtml(stateLabel(item)) + '</span>'
                + (item.inProgress ? '<span class="meter" aria-hidden="true"><span style="transform:scaleX(' + item.progress.toFixed(3) + ')"></span></span>' : '')
                + '</p>';

            var actions = '';
            if (owned) {
                actions += '<button type="button" class="btn btn-primary" data-action="play">' + ICONS.play
                    + '<span>' + escapeHtml(item.inProgress ? S.resume : S.play) + '</span></button>'
                    + '<button type="button" class="btn btn-toggle" data-action="played" aria-pressed="' + item.played + '"'
                    + ' aria-label="' + escapeHtml(item.played ? S.markUnplayed : S.markPlayed) + '" title="' + escapeHtml(item.played ? S.markUnplayed : S.markPlayed) + '">'
                    + ICONS.check + '<span>' + escapeHtml(item.played ? S.played : S.markPlayed) + '</span></button>';
            } else if (data.canRequest && !requests[item.id]) {
                actions += '<button type="button" class="btn btn-primary" data-action="request">' + ICONS.send
                    + '<span>' + escapeHtml(S.request) + '</span></button>';
            }

            return '<p class="hero-eyebrow">' + escapeHtml(eyebrow) + '</p>'
                + '<h2 class="hero-title">' + (owned
                    ? '<a class="hero-link" href="' + escapeHtml(detailsHref(item)) + '" aria-label="' + escapeHtml(format(S.openTitle, { title: item.title })) + '">'
                        + escapeHtml(item.title) + '</a>'
                    : escapeHtml(item.title)) + '</h2>'
                + '<p class="hero-meta">' + meta.map(function (m) { return '<span>' + escapeHtml(m) + '</span>'; }).join('') + '</p>'
                + state
                + (item.note ? '<p class="hero-note">' + escapeHtml(item.note) + '</p>' : '')
                + (actions ? '<div class="hero-actions">' + actions + '</div>' : '');
        }

        function renderHero(item) {
            var hero = $('.hero');
            hero.hidden = !item;
            if (!item) {
                return;
            }
            hero.classList.toggle('is-link', item.status === 'owned');
            heroToken++;
            var token = heroToken;
            var content = $('.hero-content');
            var apply = function () {
                if (token !== heroToken) {
                    return;
                }
                var poster = $('.hero-poster');
                var url = posterUrl(item, 480);
                poster.className = 'hero-poster' + (item.status === 'owned' ? '' : ' absent');
                poster.innerHTML = url ? '<img alt="" src="' + escapeHtml(url) + '">' : '';
                $('.hero-body').innerHTML = heroHtml(item);
                content.classList.remove('swapping');
            };
            if (reduceMotion.matches || content.classList.contains('swapping') || !$('.hero-body').innerHTML) {
                apply();
            } else {
                content.classList.add('swapping');
                setTimeout(apply, 160);
            }
            setBackdrop(item);
        }

        // --- actions ---

        function detailsHref(item) {
            return '#/details?id=' + item.jellyfinId + '&serverId=' + data.serverId;
        }

        function openDetails(item) {
            location.hash = detailsHref(item);
        }

        // swaps in the entry as the server now sees it
        function applyItem(updated) {
            var item = camelize(updated);
            item.seasons = item.seasons || [];
            data.items[data.items.indexOf(byId[item.id])] = item;
            byId[item.id] = item;
            visible = visible.map(function (v) { return v.id === item.id ? item : v; });
            refreshCard(item);
            updateLinks();
            renderMinimapTrack();
            updateCounter();
            if (selectedId === item.id) {
                renderHero(item);
            }
        }

        function togglePlayed(item, button) {
            button.disabled = true;
            authFetch(api + '/played/' + encodeURIComponent(item.id) + '?language=' + encodeURIComponent(language()), item.played ? 'DELETE' : 'POST').then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            }).then(applyItem).catch(function (error) {
                console.error('[MCU Timeline]', error);
                toast(S.markFailed);
                button.disabled = false;
            });
        }

        function play(item) {
            authFetch(api + '/play/' + encodeURIComponent(item.id), 'POST').then(function (response) {
                if (response.status === 409) {
                    // no session for this client on the server, the item page has a play button
                    openDetails(item);
                } else if (!response.ok) {
                    toast(S.playFailed);
                }
            }, function () {
                toast(S.playFailed);
            });
        }

        function request(item, button) {
            button.disabled = true;
            button.querySelector('span').textContent = S.requesting;
            authFetch(api + '/request/' + encodeURIComponent(item.id), 'POST').then(function (response) {
                return response.json().catch(function () { return {}; }).then(function (body) {
                    body = camelize(body);
                    if (!response.ok || !body.status) {
                        var text = body.error && S.requestErrors[body.error];
                        throw new Error(text ? format(text, { message: body.seerrMessage || '' }) : S.requestFailed);
                    }
                    requests[item.id] = body.status;
                    toast(S.requestSent);
                    refreshCard(item);
                    if (selectedId === item.id) {
                        renderHero(item);
                    }
                });
            }).catch(function (error) {
                toast(error.message || S.requestFailed);
                button.disabled = false;
                button.querySelector('span').textContent = S.request;
            });
        }

        // --- minimap ---

        function renderMinimapTrack() {
            var previous = null;
            $('.minimap-track').innerHTML = visible.map(function (item) {
                var classes = ['m-' + (item.status === 'owned' ? (item.played ? 'played' : item.inProgress ? 'progress' : 'owned') : item.status)];
                if (previous && groupOf(previous) !== groupOf(item)) {
                    classes.push('group-start');
                }
                if (item.id === selectedId) {
                    classes.push('selected');
                }
                previous = item;
                return '<span data-id="' + escapeHtml(item.id) + '" class="' + classes.join(' ') + '"></span>';
            }).join('');
        }

        function updateMinimapWindow() {
            var rail = $('.rail');
            var minimap = $('.minimap');
            var track = minimap.clientWidth;
            if (!track || !rail.scrollWidth) {
                return;
            }
            var win = $('.minimap-window');
            var width = Math.round(Math.min(track, Math.max(24, track * rail.clientWidth / rail.scrollWidth)));
            // the width only changes on resize or filter, the scroll moves it by transform
            if (win.dataset.width !== String(width)) {
                win.dataset.width = width;
                win.style.width = width + 'px';
            }
            var max = rail.scrollWidth - rail.clientWidth;
            var x = max > 0 ? (rail.scrollLeft / max) * (track - width) : 0;
            win.style.transform = 'translateX(' + x + 'px)';

            $('.rail-nav.prev').disabled = rail.scrollLeft <= 2;
            $('.rail-nav.next').disabled = rail.scrollLeft >= max - 2;
        }

        function setupMinimap() {
            var minimap = $('.minimap');
            var rail = $('.rail');
            var dragging = false;

            function jump(event, smooth) {
                var rect = minimap.getBoundingClientRect();
                var fraction = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
                rail.scrollTo({ left: fraction * rail.scrollWidth - rail.clientWidth / 2, behavior: smooth ? scrollBehavior() : 'auto' });
            }

            minimap.addEventListener('pointerdown', function (event) {
                dragging = true;
                minimap.setPointerCapture(event.pointerId);
                rail.style.scrollSnapType = 'none';
                jump(event, true);
            });
            minimap.addEventListener('pointermove', function (event) {
                if (dragging) {
                    jump(event, false);
                }
            });
            function stop() {
                dragging = false;
                rail.style.scrollSnapType = '';
            }
            minimap.addEventListener('pointerup', stop);
            minimap.addEventListener('pointercancel', stop);

            var pending = false;
            rail.addEventListener('scroll', function () {
                if (!pending) {
                    pending = true;
                    requestAnimationFrame(function () {
                        pending = false;
                        updateMinimapWindow();
                    });
                }
            }, { passive: true });

            $('.rail-nav.prev').addEventListener('click', function () {
                rail.scrollBy({ left: -rail.clientWidth * .8, behavior: scrollBehavior() });
            });
            $('.rail-nav.next').addEventListener('click', function () {
                rail.scrollBy({ left: rail.clientWidth * .8, behavior: scrollBehavior() });
            });

            if ('ResizeObserver' in window) {
                new ResizeObserver(function () {
                    updateMinimapWindow();
                    placeIndicator(false);
                    setHeaderOffset();
                }).observe(rail);
            }
        }

        // --- controls ---

        function placeIndicator(animate) {
            var group = $('.segmented');
            var checked = group.querySelector('[aria-checked="true"]');
            var indicator = group.querySelector('.segmented-indicator');
            if (!checked) {
                return;
            }
            if (!animate) {
                indicator.style.transition = 'none';
            }
            indicator.style.width = checked.offsetWidth + 'px';
            indicator.style.transform = 'translateX(' + checked.offsetLeft + 'px)';
            if (!animate) {
                indicator.getBoundingClientRect();
                indicator.style.transition = '';
            }
        }

        function setupOrder() {
            var group = $('.segmented');
            group.setAttribute('aria-label', S.orderLabel);
            group.innerHTML = '<span class="segmented-indicator" aria-hidden="true"></span>'
                + [['release', S.orderRelease], ['chrono', S.orderChrono]].map(function (option) {
                    var on = option[0] === prefs.order;
                    return '<button type="button" role="radio" data-value="' + option[0] + '" aria-checked="' + on
                        + '" tabindex="' + (on ? 0 : -1) + '">' + escapeHtml(option[1]) + '</button>';
                }).join('');
            var buttons = Array.prototype.slice.call(group.querySelectorAll('[role="radio"]'));

            function choose(button, focus) {
                if (button.getAttribute('aria-checked') === 'true') {
                    return;
                }
                buttons.forEach(function (b) {
                    var on = b === button;
                    b.setAttribute('aria-checked', on);
                    b.tabIndex = on ? 0 : -1;
                });
                if (focus) {
                    button.focus();
                }
                prefs.order = button.dataset.value;
                savePrefs();
                placeIndicator(true);
                layout(true);
                renderHero(byId[selectedId]);
            }

            group.addEventListener('click', function (event) {
                var button = event.target.closest('[role="radio"]');
                if (button) {
                    choose(button, false);
                }
            });
            group.addEventListener('keydown', function (event) {
                var index = buttons.indexOf(root.activeElement);
                var step = { ArrowRight: 1, ArrowLeft: -1 }[event.key];
                if (index >= 0 && step) {
                    event.preventDefault();
                    event.stopPropagation();
                    choose(buttons[(index + step + buttons.length) % buttons.length], true);
                }
            });
            requestAnimationFrame(function () { placeIndicator(false); });
        }

        function chipHtml(kind, value, label, pressed, ariaLabel) {
            return '<button type="button" class="chip" data-kind="' + kind + '" data-value="' + escapeHtml(value) + '" aria-pressed="' + pressed + '"'
                + (ariaLabel ? ' aria-label="' + escapeHtml(ariaLabel) + '"' : '') + '>'
                + ICONS.check.replace('class="icon"', 'class="icon check"') + '<span>' + escapeHtml(label) + '</span></button>';
        }

        function setupFilters() {
            var types = ['movie', 'series', 'short'].filter(function (type) {
                return data.items.some(function (item) { return item.type === type; });
            });
            var phases = [];
            data.items.forEach(function (item) {
                if (phases.indexOf(item.phase) < 0) {
                    phases.push(item.phase);
                }
            });
            phases.sort();

            var filters = $('.filters');
            filters.setAttribute('aria-label', S.filtersLabel);
            filters.innerHTML = '<span class="filter-group" role="group" aria-label="' + escapeHtml(S.typeLabel) + '">'
                + types.map(function (type) { return chipHtml('type', type, S.types[type], prefs.types.indexOf(type) >= 0); }).join('')
                + '</span>'
                + '<span class="filter-group" role="group" aria-label="' + escapeHtml(S.phaseLabel) + '"><span class="filter-label" aria-hidden="true">' + escapeHtml(S.phaseLabel) + '</span>'
                + phases.map(function (phase) {
                    return chipHtml('phase', phase, String(phase), prefs.phases.indexOf(phase) >= 0, format(S.phase, { n: phase }));
                }).join('')
                + '</span>'
                + '<label class="switch"><input type="checkbox" role="switch"' + (prefs.onlyOwned ? ' checked' : '') + '>'
                + '<span class="switch-track" aria-hidden="true"></span><span>' + escapeHtml(S.onlyOwned) + '</span></label>';

            filters.addEventListener('click', function (event) {
                var chip = event.target.closest('.chip');
                if (!chip) {
                    return;
                }
                var on = chip.getAttribute('aria-pressed') !== 'true';
                chip.setAttribute('aria-pressed', on);
                var list = chip.dataset.kind === 'type' ? prefs.types : prefs.phases;
                var value = chip.dataset.kind === 'type' ? chip.dataset.value : Number(chip.dataset.value);
                var at = list.indexOf(value);
                if (on && at < 0) {
                    list.push(value);
                } else if (!on && at >= 0) {
                    list.splice(at, 1);
                }
                savePrefs();
                layout(true);
            });
            filters.querySelector('input').addEventListener('change', function () {
                prefs.onlyOwned = this.checked;
                savePrefs();
                layout(true);
            });
        }

        function setupRail() {
            var rail = $('.rail');
            rail.setAttribute('aria-label', S.railLabel);
            rail.addEventListener('load', onImageEvent, true);
            rail.addEventListener('error', onImageEvent, true);

            rail.addEventListener('click', function (event) {
                var li = event.target.closest('.card');
                if (li) {
                    select(li.dataset.id, { focus: event.target.closest('.card-hit') !== null });
                }
            });

            rail.addEventListener('keydown', function (event) {
                var li = event.target.closest('.card');
                if (!li || !event.target.classList.contains('card-hit')) {
                    return;
                }
                var index = visible.findIndex(function (item) { return item.id === li.dataset.id; });
                var forward = phone.matches ? 'ArrowDown' : 'ArrowRight';
                var back = phone.matches ? 'ArrowUp' : 'ArrowLeft';
                var target = null;
                if (event.key === forward) {
                    target = visible[Math.min(visible.length - 1, index + 1)];
                } else if (event.key === back) {
                    target = visible[Math.max(0, index - 1)];
                } else if (event.key === 'Home') {
                    target = visible[0];
                } else if (event.key === 'End') {
                    target = visible[visible.length - 1];
                } else if (event.key === 'Enter') {
                    var item = byId[li.dataset.id];
                    event.preventDefault();
                    event.stopPropagation();
                    if (item.status === 'owned') {
                        openDetails(item);
                    } else {
                        select(item.id);
                    }
                    return;
                } else {
                    return;
                }
                // the web client moves focus on arrows too, on TV
                event.preventDefault();
                event.stopPropagation();
                select(target.id, { focus: true, scroll: true });
            });
        }

        function setupHero() {
            $('.hero').addEventListener('click', function (event) {
                var item = byId[selectedId];
                if (!item || event.target.closest('a')) {
                    return;
                }
                var button = event.target.closest('[data-action]');
                if (!button) {
                    if (item.status === 'owned' && !event.target.closest('button')) {
                        openDetails(item);
                    }
                    return;
                }
                if (button.dataset.action === 'play') {
                    play(item);
                } else if (button.dataset.action === 'played') {
                    togglePlayed(item, button);
                } else if (button.dataset.action === 'request') {
                    request(item, button);
                }
            });
            $('.hero').addEventListener('load', function (event) {
                if (event.target.tagName === 'IMG') {
                    event.target.classList.add('loaded');
                }
            }, true);
            $('.hero').addEventListener('error', function (event) {
                if (event.target.tagName === 'IMG') {
                    event.target.remove();
                }
            }, true);
        }

        // --- info dialog ---

        function infoHtml() {
            var I = S.info;
            var byPhase = {};
            data.items.forEach(function (item) {
                (byPhase[item.phase] = byPhase[item.phase] || []).push(item);
            });

            var years = yearsOf;

            function phaseLine(phase) {
                var items = byPhase[phase].slice().sort(function (a, b) { return a.releaseRank - b.releaseRank; });
                // a phase is known by its films, the shorts in between would be odd bookends
                var films = items.filter(function (item) { return item.type === 'movie'; });
                var ends = films.length ? films : items;
                return '<li><span class="term">' + escapeHtml(format(S.phase, { n: phase })) + '</span><span class="desc">'
                    + escapeHtml(format(I.phaseLine, { years: years(items), count: items.length, first: ends[0].title, last: ends[ends.length - 1].title }))
                    + '</span></li>';
            }

            var sagas = ['infinity', 'multiverse'].filter(function (saga) {
                return data.items.some(function (item) { return item.saga === saga; });
            });

            var sagaList = sagas.map(function (saga) {
                var items = data.items.filter(function (item) { return item.saga === saga; });
                var phases = Object.keys(byPhase).map(Number).filter(function (p) {
                    return byPhase[p].some(function (item) { return item.saga === saga; });
                }).sort();
                return '<li><span class="term">' + escapeHtml(S.sagas[saga]) + '</span><span class="desc">'
                    + escapeHtml(I.sagaText[saga]) + ' ' + escapeHtml(format(S.phaseRange, { from: phases[0], to: phases[phases.length - 1] }) + ', ' + years(items) + '.')
                    + '</span></li>';
            }).join('');

            var phaseList = Object.keys(byPhase).map(Number).sort().map(phaseLine).join('');

            var eraList = Object.keys(S.eras).map(function (era) {
                return '<li><span class="term">' + escapeHtml(S.eras[era].name) + '</span><span class="desc">' + escapeHtml(S.eras[era].range) + '</span></li>';
            }).join('');

            var typeList = ['movie', 'series', 'short'].map(function (type) {
                return '<li><span class="term">' + escapeHtml(S.types[type]) + '</span><span class="desc">' + escapeHtml(I.typeText[type]) + '</span></li>';
            }).join('');

            function legend(icon, text, extra) {
                return '<li><span class="term legend-swatch">' + (icon ? '<span class="badge">' + icon + '</span>' : '') + (extra || '')
                    + '</span><span class="desc">' + escapeHtml(text) + '</span></li>';
            }

            return '<div class="info-head"><h2 id="mcu-info-title">' + escapeHtml(I.title) + '</h2>'
                + '<button type="button" class="btn btn-icon" data-close aria-label="' + escapeHtml(S.close) + '">' + ICONS.close + '</button></div>'
                + '<div class="info-body">'
                + '<h3>' + escapeHtml(I.ordersTitle) + '</h3><ul class="info-list">'
                + '<li><span class="term">' + escapeHtml(S.orderRelease) + '</span><span class="desc">' + escapeHtml(I.orderRelease) + '</span></li>'
                + '<li><span class="term">' + escapeHtml(S.orderChrono) + '</span><span class="desc">' + escapeHtml(I.orderChrono) + '</span></li></ul>'
                + '<h3>' + escapeHtml(I.sagasTitle) + '</h3><p>' + escapeHtml(I.sagasIntro) + '</p><ul class="info-list">' + sagaList + '</ul>'
                + '<h3>' + escapeHtml(I.phasesTitle) + '</h3><p>' + escapeHtml(I.phasesIntro) + '</p><ul class="info-list">' + phaseList + '</ul>'
                + '<h3>' + escapeHtml(I.erasTitle) + '</h3><p>' + escapeHtml(I.erasIntro) + '</p><ul class="info-list">' + eraList + '</ul>'
                + '<h3>' + escapeHtml(I.typesTitle) + '</h3><ul class="info-list">' + typeList + '</ul>'
                + '<h3>' + escapeHtml(I.legendTitle) + '</h3><ul class="info-list">'
                + legend(null, I.legend.owned, escapeHtml(S.statusOwned))
                + legend(null, I.legend.progress, escapeHtml(S.resume))
                + legend(ICONS.check, I.legend.played, escapeHtml(S.played))
                + legend(ICONS.missing, I.legend.absent, escapeHtml(S.statusAbsent))
                + legend(ICONS.hourglass, I.legend.requested, escapeHtml(S.requestStatus.pending))
                + legend(ICONS.clock, I.legend.upcoming, escapeHtml(S.statusUpcoming))
                + '</ul>'
                + '<h3>' + escapeHtml(I.keysTitle) + '</h3><p>' + escapeHtml(I.keys) + '</p>'
                + '</div>';
        }

        function setupInfo() {
            var dialog = $('.info');
            var opener = $('.info-open');
            opener.setAttribute('aria-label', S.infoButton);
            opener.title = S.infoButton;
            opener.addEventListener('click', function () {
                dialog.innerHTML = infoHtml();
                dialog.showModal();
            });
            dialog.addEventListener('click', function (event) {
                // a click on the backdrop lands on the dialog itself
                if (event.target === dialog || event.target.closest('[data-close]')) {
                    dialog.close();
                }
            });
        }

        // --- build ---

        function build() {
            root.innerHTML = '<style>' + css + '</style>'
                + '<div class="mcu-timeline">'
                + '<section class="hero" aria-label="' + escapeHtml(S.pageTitle) + '">'
                + '<div class="hero-bg" aria-hidden="true"><div class="hero-bg-layer"></div><div class="hero-bg-layer"></div></div>'
                + '<div class="hero-inner hero-content"><div class="hero-poster"></div><div class="hero-body"></div></div>'
                + '</section>'
                + '<div class="controls">'
                + '<div class="segmented" role="radiogroup"></div>'
                + '<span class="counter" aria-live="polite"></span>'
                + '<span class="controls-spacer"></span>'
                + '<button type="button" class="btn btn-icon info-open">' + ICONS.info + '</button>'
                + '</div>'
                + '<div class="filters" role="group"></div>'
                + '<p class="message" role="status"></p>'
                + '<div class="rail-wrap">'
                + '<button type="button" class="btn btn-icon rail-nav prev" aria-label="' + escapeHtml(S.previous) + '">' + ICONS.left + '</button>'
                + '<ol class="rail"></ol>'
                + '<button type="button" class="btn btn-icon rail-nav next" aria-label="' + escapeHtml(S.next) + '">' + ICONS.right + '</button>'
                + '</div>'
                + '<div class="minimap" aria-hidden="true"><div class="minimap-track"></div><div class="minimap-window"></div></div>'
                + '<div class="minimap-legend" aria-hidden="true">'
                + [['played', S.played], ['progress', S.inProgressShort], ['owned', S.toWatch], ['absent', S.statusAbsent], ['upcoming', S.statusUpcoming]]
                    .map(function (entry) { return '<span><i class="swatch m-' + entry[0] + '"></i>' + escapeHtml(entry[1]) + '</span>'; }).join('')
                + '</div>'
                + '<dialog class="info" aria-labelledby="mcu-info-title"></dialog>'
                + '</div>';

            setupOrder();
            setupFilters();
            setupRail();
            setupHero();
            setupInfo();
            setupMinimap();
            setupReveal();
            createCards();
        }

        // --- data ---

        function loadExtras() {
            authFetch(api + '/posters').then(function (r) { return r.ok ? r.json() : {}; }).then(function (result) {
                posters = result || {};
                data.items.forEach(function (item) {
                    if (item.status !== 'owned' && posters[item.id]) {
                        refreshCard(item);
                    }
                });
                if (selectedId && byId[selectedId].status !== 'owned') {
                    renderHero(byId[selectedId]);
                }
            }).catch(function (error) {
                console.warn('[MCU Timeline] posters', error);
            });

            if (!data.canRequest) {
                return;
            }
            authFetch(api + '/requests').then(function (r) { return r.ok ? r.json() : {}; }).then(function (result) {
                requests = result || {};
                Object.keys(requests).forEach(function (id) {
                    if (byId[id]) {
                        refreshCard(byId[id]);
                    }
                });
                if (selectedId && requests[selectedId]) {
                    renderHero(byId[selectedId]);
                }
            }).catch(function (error) {
                console.warn('[MCU Timeline] requests', error);
            });
        }

        function load() {
            if (!client()) {
                showMessage(S ? S.loginRequired : 'MCU Timeline');
                return;
            }
            api = client().getUrl('McuTimeline');

            authFetch(api + '/items?language=' + encodeURIComponent(language())).then(function (response) {
                if (response.status === 401 || response.status === 403) {
                    showMessage(S.loginRequired);
                    return null;
                }
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            }).then(function (result) {
                if (!result) {
                    return;
                }
                var previous = selectedId;
                data = camelize(result);
                byId = {};
                data.items.forEach(function (item) {
                    item.seasons = item.seasons || [];
                    byId[item.id] = item;
                });
                loadPrefs();
                build();
                // the cards are new, layout picks the title to watch next
                selectedId = null;
                layout(false);
                if (previous && previous !== selectedId && visible.some(function (item) { return item.id === previous; })) {
                    select(previous, { scroll: true, instant: true });
                }
                setPageTitle();
                loadExtras();
            }).catch(function (error) {
                console.error('[MCU Timeline]', error);
                showMessage(S.loadFailed, true);
            });
        }

        // a page kept in the web client's view cache comes back without reloading the
        // fragment, refresh the play states then
        var page = host.closest('[data-role="page"]');
        if (page) {
            page.addEventListener('viewshow', function (event) {
                if (event.detail && event.detail.isRestored && data) {
                    load();
                }
            });
        }

        setHeaderOffset();
        loadAssets().then(load, function (error) {
            console.error('[MCU Timeline]', error);
            root.textContent = 'MCU Timeline: the timeline assets could not be loaded.';
        });
    }

    window.McuTimeline = {
        mountPending: function () {
            Array.prototype.forEach.call(document.querySelectorAll('.mcuTimelineHost:not([data-mcu-mounted])'), mount);
        }
    };
    window.McuTimeline.mountPending();
})();
