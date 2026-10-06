(function () {
    'use strict';

    // loaded with the web client through File Transformation. Lists the timeline under
    // Media in the side menu, the page itself comes from Plugin Pages

    var HREF = '#/userpluginsettings.html?pageUrl=/McuTimeline/page';
    var assets = document.currentScript.src.replace(/[^/]*$/, '');
    var settings = null;
    var settingsFor = null;

    function onPage() {
        return /pageUrl=(%2F|\/)McuTimeline(%2F|\/)page/i.test(location.hash);
    }

    function fetchJson(url, token) {
        return fetch(url, { headers: { Authorization: 'MediaBrowser Token="' + token + '"' } }).then(function (response) {
            if (!response.ok) {
                throw new Error(url + ': HTTP ' + response.status);
            }
            return response.json();
        });
    }

    // once per signed in user: whether to show the entry, and its label
    function loadSettings() {
        var client = window.ApiClient;
        var token = client && client.accessToken && client.accessToken();
        if (!token) {
            return null;
        }
        if (settingsFor !== token) {
            settingsFor = token;
            settings = Promise.all([
                fetchJson(client.getUrl('McuTimeline/menu'), token),
                fetchJson(assets + (/^fr\b/i.test(document.documentElement.lang || '') ? 'strings-fr.json' : 'strings-en.json'), token)
            ]).then(function (results) {
                return { enabled: results[0].Enabled, label: results[1].pageTitle };
            }).catch(function (error) {
                console.warn('[MCU Timeline] menu', error);
                return { enabled: false };
            });
        }
        return settings;
    }

    function update() {
        var menu = document.querySelector('.libraryMenuOptions');
        // filled once the user's libraries arrive, the entry goes after them
        if (!menu || !menu.querySelector('.sidebarHeader')) {
            return;
        }
        var pending = loadSettings();
        if (!pending) {
            return;
        }
        pending.then(function (result) {
            var link = menu.querySelector('.mcuTimelineLink');
            if (!result.enabled) {
                if (link) {
                    link.remove();
                }
                return;
            }
            if (!link) {
                link = document.createElement('a');
                link.className = 'navMenuOption lnkMediaFolder mcuTimelineLink';
                link.href = HREF;
                link.innerHTML = '<span class="material-icons navMenuOptionIcon timeline" aria-hidden="true"></span>'
                    + '<span class="sectionName navMenuOptionText"></span>';
                menu.appendChild(link);
            }
            // guarded, every write wakes the observer below
            var label = link.querySelector('.navMenuOptionText');
            if (label.textContent !== result.label) {
                label.textContent = result.label;
            }
            if (link.classList.contains('navMenuOption-selected') !== onPage()) {
                link.classList.toggle('navMenuOption-selected', onPage());
            }
        });
    }

    new MutationObserver(update).observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', update);
    // the web client clears the selection on each page change, put it back after it
    document.addEventListener('pageshow', function () { setTimeout(update, 0); });
    update();
})();
