(function () {
    'use strict';

    // the page is served at <base>/McuTimeline/page, base is empty unless the server has a
    // base URL
    var base = location.pathname.replace(/\/McuTimeline\/page.*$/i, '');
    var api = base + '/McuTimeline';

    var ICONS = {
        play: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M8 5v14l11-7z"/></svg>',
        check: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z"/></svg>',
        missing: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 0 1-6.3-12.9l11.2 11.2A8 8 0 0 1 12 20zm6.3-3.1L7.1 5.7A8 8 0 0 1 18.3 16.9z"/></svg>',
        clock: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16zm.5-13H11v6l5.2 3.2.8-1.3-4.5-2.7z"/></svg>',
        info: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M11 7h2v2h-2zm0 4h2v6h-2zm1-9a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16z"/></svg>',
        silhouette: '<svg class="silhouette" viewBox="0 0 60 90" preserveAspectRatio="xMidYMid slice" aria-hidden="true"><circle cx="30" cy="34" r="10" fill="currentColor"/><path d="M12 72c0-11 8-19 18-19s18 8 18 19z" fill="currentColor"/></svg>'
    };

    var $ = function (id) { return document.getElementById(id); };
    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

    var S = {};
    var auth = null;
    var data = null;
    var prefs = { order: 'release', type: 'all', hideMissing: false };

    // --- web client integration ---

    function sameOriginParent() {
        try {
            return window.parent !== window && window.parent.document ? window.parent : null;
        } catch (e) {
            return null;
        }
    }

    // the timeline is only for people signed in to this server: it borrows the web
    // client session, and every data call below is refused without it
    function findCredentials() {
        var parent = sameOriginParent();
        try {
            var client = parent && parent.ApiClient;
            if (client && client.accessToken && client.accessToken()) {
                return { token: client.accessToken() };
            }
        } catch (e) {
            // fall through to the stored credentials
        }

        try {
            var stored = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            var servers = (stored.Servers || []).filter(function (s) { return s.AccessToken; });
            servers.sort(function (a, b) { return (b.DateLastAccessed || 0) - (a.DateLastAccessed || 0); });
            return servers.length ? { token: servers[0].AccessToken } : null;
        } catch (e) {
            return null;
        }
    }

    function adoptTheme() {
        var parent = sameOriginParent();
        if (!parent) {
            return;
        }

        try {
            var source = parent.getComputedStyle(parent.document.documentElement);
            var body = parent.getComputedStyle(parent.document.body);
            var map = {
                '--bg': '--jf-palette-background-default',
                '--paper': '--jf-palette-background-paper',
                '--accent': '--jf-palette-primary-main',
                '--accent-contrast': '--jf-palette-primary-contrastText',
                '--text': '--jf-palette-text-primary',
                '--text-dim': '--jf-palette-text-secondary',
                '--divider': '--jf-palette-divider'
            };
            Object.keys(map).forEach(function (name) {
                var value = source.getPropertyValue(map[name]).trim();
                if (value) {
                    document.documentElement.style.setProperty(name, value);
                }
            });
            if (body.fontFamily) {
                document.documentElement.style.setProperty('--font', body.fontFamily);
            }
        } catch (e) {
            // keep the built in dark theme
        }
    }

    function request(method, path) {
        return fetch(api + path, {
            method: method,
            headers: { Authorization: 'MediaBrowser Token="' + auth.token + '"' },
            credentials: 'same-origin'
        });
    }

    function openDetails(item) {
        var hash = '#/details?id=' + item.jellyfinId + '&serverId=' + data.serverId;
        var parent = sameOriginParent();
        if (parent && /\/web\/?$/.test(parent.location.pathname.replace(/index\.html$/, ''))) {
            parent.location.hash = hash;
        } else {
            window.top.location.href = base + '/web/' + hash;
        }
    }

    function play(item) {
        request('POST', '/play/' + encodeURIComponent(item.id)).then(function (response) {
            if (response.status === 409) {
                // no web client session behind this page, the item page has a play button
                openDetails(item);
            } else if (!response.ok) {
                toast(S.playFailed);
            }
        }, function () {
            toast(S.playFailed);
        });
    }

    // --- formatting ---

    function format(template, values) {
        return template.replace(/\{(\w+)\}/g, function (match, key) {
            return values[key] !== undefined ? values[key] : match;
        });
    }

    function year(item) {
        return item.releaseDate.slice(0, 4);
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

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Jellyfin serialises PascalCase, the page reads the same names as the data file
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

    // --- preferences, kept per user in this browser ---

    function prefsKey() {
        return 'mcuTimeline.prefs.' + data.userId;
    }

    function loadPrefs() {
        try {
            var saved = JSON.parse(localStorage.getItem(prefsKey()) || '{}');
            if (saved.order === 'release' || saved.order === 'chrono') {
                prefs.order = saved.order;
            }
            if (['all', 'movies', 'series'].indexOf(saved.type) >= 0) {
                prefs.type = saved.type;
            }
            prefs.hideMissing = saved.hideMissing === true;
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

    // --- ordering and filtering, same rules as TimelineSorter.cs ---

    function compare(a, b) {
        var first = prefs.order === 'release'
            ? a.releaseDate.localeCompare(b.releaseDate) || a.chronoOrder - b.chronoOrder
            : a.chronoOrder - b.chronoOrder || a.releaseDate.localeCompare(b.releaseDate);
        return first || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
    }

    function visibleItems() {
        return data.items.filter(function (item) {
            if (prefs.type === 'movies' && item.type === 'series') {
                return false;
            }
            if (prefs.type === 'series' && item.type !== 'series') {
                return false;
            }
            return !(prefs.hideMissing && item.status !== 'owned');
        }).sort(compare);
    }

    function nextToWatch(items) {
        for (var i = 0; i < items.length; i++) {
            if (items[i].status === 'owned' && !items[i].played) {
                return items[i];
            }
        }
        return null;
    }

    // --- rendering ---

    function separatorsFor(item, previous) {
        var html = '';
        if (prefs.order === 'release') {
            if (!previous || previous.saga !== item.saga) {
                html += '<li class="separator"><h2>' + escapeHtml(S.sagas[item.saga] || item.saga) + '</h2></li>';
            }
            if (!previous || previous.saga !== item.saga || previous.phase !== item.phase) {
                html += '<li class="separator"><h3>' + format(S.phase, { n: item.phase }) + '</h3></li>';
            }
        } else if (!previous || previous.era !== item.era) {
            var era = S.eras[item.era] || { name: item.era, range: '' };
            html += '<li class="separator"><h2>' + escapeHtml(era.name)
                + (era.range ? '<span class="range">' + escapeHtml(era.range) + '</span>' : '') + '</h2></li>';
        }
        return html;
    }

    function posterHtml(item) {
        var html = '<div class="poster">';
        if (item.status === 'owned') {
            html += '<img loading="lazy" decoding="async" alt="" src="' + base + '/Items/' + item.jellyfinId
                + '/Images/Primary?fillWidth=240&quality=85">';
            if (item.played) {
                html += '<span class="status-icon" title="' + S.played + '">' + ICONS.check + '</span>';
            }
            if (item.inProgress) {
                html += '<span class="progress" aria-hidden="true"><span style="width:'
                    + Math.round(item.progress * 100) + '%"></span></span>';
            }
        } else {
            html += ICONS.silhouette;
            html += '<span class="status-icon" title="' + (item.status === 'upcoming' ? S.statusUpcoming : S.statusAbsent) + '">'
                + (item.status === 'upcoming' ? ICONS.clock : ICONS.missing) + '</span>';
            if (item.status === 'upcoming') {
                html += '<span class="upcoming-badge">' + escapeHtml(format(S.upcomingOn, { date: longDate(item.releaseDate) })) + '</span>';
            }
        }
        return html + '</div>';
    }

    function detailsHtml(item) {
        var lines = [];
        if (prefs.order === 'chrono' && item.storyYear) {
            lines.push(format(S.storyYear, { year: escapeHtml(item.storyYear) }));
        }
        if (item.status === 'owned') {
            if (item.type === 'series') {
                lines.push(item.episodeCount === 1 ? S.episode : format(S.episodes, { count: item.episodeCount }));
            } else if (item.runTimeTicks) {
                lines.push(runtime(item.runTimeTicks));
            }
            if (item.played) {
                lines.push(S.played);
            } else if (item.inProgress) {
                lines.push(S.inProgress + ' · ' + Math.round(item.progress * 100) + ' %');
            }
        } else {
            lines.push(item.status === 'upcoming' ? format(S.upcomingOn, { date: longDate(item.releaseDate) }) : S.statusAbsent);
        }
        if (item.type === 'series' && item.seasons.length) {
            lines.push(format(S.seasons, { list: item.seasons.join(', ') }));
        }
        return '<ul class="details">' + lines.map(function (line) { return '<li>' + line + '</li>'; }).join('') + '</ul>';
    }

    function entryHtml(item, side, current) {
        var owned = item.status === 'owned';
        var title = escapeHtml(item.title);
        var html = '<li class="entry status-' + item.status + ' saga-' + item.saga + ' side-' + side
            + (current ? ' current' : '') + '" data-id="' + escapeHtml(item.id) + '">'
            + '<span class="dot" aria-hidden="true"></span>'
            + '<article class="card"' + (owned ? ' tabindex="0" role="link" aria-label="' + escapeHtml(format(S.openTitle, { title: item.title })) + '"' : '') + '>'
            + posterHtml(item)
            + '<div class="body">'
            + '<h3 class="title">' + title + '</h3>'
            + '<p class="meta"><span>' + year(item) + '</span><span class="phase">' + format(S.phase, { n: item.phase }) + '</span>'
            + '<span class="sr-only">' + (owned ? S.statusOwned : item.status === 'upcoming' ? S.statusUpcoming : S.statusAbsent) + '</span></p>'
            + detailsHtml(item)
            + '<div class="actions">';
        if (owned) {
            html += '<button type="button" class="play" aria-label="' + escapeHtml(format(S.playTitle, { title: item.title })) + '">'
                + ICONS.play + '<span>' + S.play + '</span></button>';
        }
        if (item.note) {
            var tipId = 'note-' + item.id;
            html += '<button type="button" class="note" aria-label="' + S.noteLabel + '" aria-describedby="' + escapeHtml(tipId) + '">'
                + ICONS.info + '<span class="tooltip" role="tooltip" id="' + escapeHtml(tipId) + '">' + escapeHtml(item.note) + '</span></button>';
        }
        return html + '</div></div></article></li>';
    }

    function render() {
        var items = visibleItems();
        var next = nextToWatch(items);
        var before = capturePositions();

        var html = '';
        var previous = null;
        items.forEach(function (item, index) {
            html += separatorsFor(item, previous);
            html += entryHtml(item, index % 2 === 0 ? 'left' : 'right', next && next.id === item.id);
            previous = item;
        });

        var list = $('timeline');
        list.innerHTML = html;
        list.hidden = items.length === 0;
        $('message').textContent = items.length ? '' : S.empty;

        var owned = items.filter(function (item) { return item.status === 'owned'; });
        var seen = owned.filter(function (item) { return item.played; }).length;
        $('counter').textContent = format(S.counter, { seen: seen, total: owned.length });
        $('continue').disabled = !next;

        animateFrom(before);
    }

    // FLIP: cards slide from where they were to where the new order puts them
    function capturePositions() {
        var positions = {};
        Array.prototype.forEach.call(document.querySelectorAll('.entry'), function (el) {
            positions[el.dataset.id] = el.getBoundingClientRect().top;
        });
        return positions;
    }

    function animateFrom(before) {
        if (reduceMotion.matches || !Object.keys(before).length) {
            return;
        }

        var viewport = window.innerHeight;
        var moved = [];
        Array.prototype.forEach.call(document.querySelectorAll('.entry'), function (el) {
            var old = before[el.dataset.id];
            if (old === undefined) {
                return;
            }
            var top = el.getBoundingClientRect().top;
            var delta = old - top;
            // only what is on screen is worth animating
            if (delta && Math.abs(delta) < viewport * 2 && top < viewport && top > -viewport) {
                el.style.transform = 'translateY(' + delta + 'px)';
                moved.push(el);
            }
        });

        if (!moved.length) {
            return;
        }

        requestAnimationFrame(function () {
            moved.forEach(function (el) {
                el.classList.add('moving');
                el.style.transform = '';
            });
            setTimeout(function () {
                moved.forEach(function (el) { el.classList.remove('moving'); });
            }, 300);
        });
    }

    function toast(text) {
        var el = $('toast');
        el.textContent = text;
        el.classList.add('visible');
        clearTimeout(toast.timer);
        toast.timer = setTimeout(function () { el.classList.remove('visible'); }, 3000);
    }

    // --- controls ---

    function buildRadioGroup(container, label, options, current, onChange) {
        container.setAttribute('aria-label', label);
        container.innerHTML = options.map(function (option) {
            var checked = option.value === current;
            return '<button type="button" role="radio" data-value="' + option.value + '" aria-checked="' + checked
                + '" tabindex="' + (checked ? 0 : -1) + '">' + escapeHtml(option.label) + '</button>';
        }).join('');

        var buttons = Array.prototype.slice.call(container.querySelectorAll('[role="radio"]'));
        function select(button, focus) {
            buttons.forEach(function (b) {
                var on = b === button;
                b.setAttribute('aria-checked', on);
                b.tabIndex = on ? 0 : -1;
            });
            if (focus) {
                button.focus();
            }
            onChange(button.dataset.value);
        }

        container.addEventListener('click', function (event) {
            var button = event.target.closest('[role="radio"]');
            if (button && button.getAttribute('aria-checked') !== 'true') {
                select(button, false);
            }
        });
        container.addEventListener('keydown', function (event) {
            var index = buttons.indexOf(document.activeElement);
            if (index < 0) {
                return;
            }
            var step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
            if (step) {
                event.preventDefault();
                select(buttons[(index + step + buttons.length) % buttons.length], true);
            }
        });
    }

    function setupControls() {
        buildRadioGroup($('order'), S.orderLabel, [
            { value: 'release', label: S.orderRelease },
            { value: 'chrono', label: S.orderChrono }
        ], prefs.order, function (value) {
            prefs.order = value;
            savePrefs();
            render();
        });

        buildRadioGroup($('type'), S.typeLabel, [
            { value: 'all', label: S.typeAll },
            { value: 'movies', label: S.typeMovies },
            { value: 'series', label: S.typeSeries }
        ], prefs.type, function (value) {
            prefs.type = value;
            savePrefs();
            render();
        });

        $('hideMissingLabel').textContent = S.hideMissing;
        $('hideMissing').checked = prefs.hideMissing;
        $('hideMissing').addEventListener('change', function () {
            prefs.hideMissing = this.checked;
            savePrefs();
            render();
        });

        $('continue').textContent = S.continue;
        $('continue').addEventListener('click', function () {
            var current = document.querySelector('.entry.current');
            if (!current) {
                toast(S.allSeen);
                return;
            }
            current.scrollIntoView({ behavior: reduceMotion.matches ? 'auto' : 'smooth', block: 'center' });
            var card = current.querySelector('.card');
            card.focus({ preventScroll: true });
        });

        var list = $('timeline');
        list.addEventListener('click', function (event) {
            var entry = event.target.closest('.entry.status-owned');
            if (!entry || event.target.closest('.note')) {
                return;
            }
            var item = findItem(entry.dataset.id);
            if (event.target.closest('.play')) {
                play(item);
            } else {
                openDetails(item);
            }
        });
        list.addEventListener('keydown', function (event) {
            if ((event.key === 'Enter' || event.key === ' ') && event.target.classList.contains('card')) {
                event.preventDefault();
                openDetails(findItem(event.target.closest('.entry').dataset.id));
            }
        });

        $('controls').hidden = false;
    }

    function findItem(id) {
        for (var i = 0; i < data.items.length; i++) {
            if (data.items[i].id === id) {
                return data.items[i];
            }
        }
        return null;
    }

    // --- start ---

    function showMessage(text, action) {
        var message = $('message');
        message.textContent = text;
        if (action) {
            message.appendChild(document.createElement('br'));
            message.appendChild(action);
        }
    }

    function requireLogin() {
        var link = document.createElement('a');
        link.href = base + '/web/';
        link.target = '_top';
        link.textContent = S.login;
        showMessage(S.loginRequired, link);
    }

    function load() {
        showMessage(S.loading);
        auth = findCredentials();
        if (!auth) {
            requireLogin();
            return;
        }

        request('GET', '/items').then(function (response) {
            if (response.status === 401 || response.status === 403) {
                requireLogin();
                return null;
            }
            if (!response.ok) {
                throw new Error(response.status);
            }
            return response.json();
        }).then(function (result) {
            if (!result) {
                return;
            }
            data = camelize(result);
            data.items.forEach(function (item) {
                item.seasons = item.seasons || [];
            });
            loadPrefs();
            showMessage('');
            setupControls();
            render();
        }).catch(function (error) {
            console.error('[MCU Timeline]', error);
            var retry = document.createElement('button');
            retry.type = 'button';
            retry.textContent = S.retry;
            retry.addEventListener('click', load);
            showMessage(S.loadFailed, retry);
        });
    }

    adoptTheme();
    fetch(api + '/assets/strings-fr.json').then(function (response) {
        if (!response.ok) {
            throw new Error(response.status);
        }
        return response.json();
    }).then(function (strings) {
        S = strings;
        document.documentElement.lang = S.locale.slice(0, 2);
        document.title = S.pageTitle;
        load();
    }).catch(function () {
        // nothing translated to show yet, a bare message beats a blank page
        $('message').textContent = 'MCU Timeline: strings-fr.json could not be loaded.';
    });
})();
