// Общие скрипты приложения + логика формы рабочего дня.

window.workTime = {
    parseHours: function (s) {
        var v = parseFloat(String(s).replace(',', '.'));
        return isNaN(v) ? 0 : v;
    },
    parseMinutes: function (s) {
        var m = /^(\d{1,2}):(\d{2})$/.exec(String(s || ''));
        return m ? (+m[1]) * 60 + (+m[2]) : 0;
    }
};

(function () {
    var draftPrefix = 'workTimeTracker.draft.';
    var draftDelay = 400;
    var formStates = [];
    var draftGroups = {};
    var stateByContainer = new WeakMap();

    function recalc(container) {
        if (!container) return;
        var start = container.querySelector('.day-start');
        var end = container.querySelector('.day-end');
        var workedEl = container.querySelector('.worked-hours');
        var sumEl = container.querySelector('.entries-hours-sum');
        if (!start || !end || !workedEl) return;

        var sm = window.workTime.parseMinutes(start.value);
        var em = window.workTime.parseMinutes(end.value);
        var worked = em > sm ? Math.max(0, (em - sm) / 60 - 1) : 0;
        workedEl.textContent = Math.round(worked * 100) / 100;

        var sum = 0;
        container.querySelectorAll('.hours-input').forEach(function (inp) {
            sum += window.workTime.parseHours(inp.value);
        });
        sum = Math.round(sum * 100) / 100;
        if (!sumEl) return;
        sumEl.textContent = sum;

        var invalid = Math.abs(sum - worked) > 0.001;
        sumEl.classList.toggle('hours-sum-invalid', invalid);
        sumEl.classList.toggle('hours-sum-valid', !invalid && sum > 0);
        var table = container.querySelector('.entries-table');
        if (table) table.classList.toggle('table-overtime', invalid);
    }

    function renumberRows(container) {
        container.querySelectorAll('tr.entries-row').forEach(function (row, index) {
            row.querySelectorAll('[name]').forEach(function (input) {
                input.name = input.name.replace(/Input\.Entries\[\d+\]/, 'Input.Entries[' + index + ']');
            });
        });
    }

    function serializeForm(container) {
        var data = {
            startTime: container.querySelector('.day-start').value,
            endTime: container.querySelector('.day-end').value,
            entries: []
        };
        container.querySelectorAll('tr.entries-row').forEach(function (row) {
            data.entries.push({
                boatId: row.querySelector('.boat-select').value,
                hours: row.querySelector('.hours-input').value,
                workDescription: row.querySelector('.work-description').value,
                isInhouse: row.querySelector('.inhouse-check').checked
            });
        });
        return data;
    }

    function canonicalForm(data) {
        return {
            startTime: data.startTime,
            endTime: data.endTime,
            entries: data.entries.filter(function (entry) {
                return window.workTime.parseHours(entry.hours) > 0
                    || entry.workDescription.trim().length > 0
                    || entry.boatId.length > 0;
            }).map(function (entry) {
                return {
                    boatId: entry.isInhouse ? '' : entry.boatId,
                    hours: String(window.workTime.parseHours(entry.hours)),
                    workDescription: entry.workDescription.trim(),
                    isInhouse: entry.isInhouse
                };
            })
        };
    }

    function isValidDraft(data) {
        return data && typeof data.startTime === 'string' && typeof data.endTime === 'string'
            && Array.isArray(data.entries) && data.entries.every(function (entry) {
                return entry && typeof entry.boatId === 'string' && typeof entry.hours === 'string'
                    && typeof entry.workDescription === 'string' && typeof entry.isInhouse === 'boolean';
            });
    }

    function readDraftGroup(key) {
        var empty = { version: 1, forms: {} };
        try {
            var value = localStorage.getItem(key);
            if (!value) return empty;
            var data = JSON.parse(value);
            return data && data.version === 1 && data.forms && typeof data.forms === 'object' ? data : empty;
        } catch (_) {
            return empty;
        }
    }

    function writeDraftGroup(key, data) {
        try {
            if (Object.keys(data.forms).length) localStorage.setItem(key, JSON.stringify(data));
            else localStorage.removeItem(key);
        } catch (_) {
        }
    }

    function getDraftGroup(key) {
        if (!draftGroups[key]) {
            draftGroups[key] = { key: key, states: [], timer: null, data: readDraftGroup(key) };
        }
        return draftGroups[key];
    }

    function saveDraftGroup(group) {
        if (group.timer) {
            clearTimeout(group.timer);
            group.timer = null;
        }
        var data = readDraftGroup(group.key);
        group.states.forEach(function (state) {
            if (!state.touched || state.draftDisabled) return;
            if (state.dirty) {
                var draft = serializeForm(state.container);
                draft.savedAt = new Date().toISOString();
                data.forms[state.date] = draft;
                state.draft = draft;
            } else {
                delete data.forms[state.date];
                state.draft = null;
            }
            state.touched = false;
        });
        writeDraftGroup(group.key, data);
        group.data = data;
    }

    function scheduleDraftSave(state) {
        if (!state.group) return;
        if (state.group.timer) clearTimeout(state.group.timer);
        state.group.timer = setTimeout(function () { saveDraftGroup(state.group); }, draftDelay);
    }

    function hideDraftBanner(state) {
        if (state.banner) state.banner.remove();
        state.banner = null;
    }

    function deleteDraft(state) {
        if (!state.group) return;
        state.draftDisabled = true;
        state.touched = false;
        saveDraftGroup(state.group);
        var data = readDraftGroup(state.group.key);
        delete data.forms[state.date];
        writeDraftGroup(state.group.key, data);
        state.group.data = data;
        state.draft = null;
        hideDraftBanner(state);
    }

    function updateDirty(state) {
        state.dirty = JSON.stringify(serializeForm(state.container)) !== state.initialSnapshot;
        state.indicator.hidden = !state.dirty;
        state.form.classList.toggle('has-unsaved-changes', state.dirty);
    }

    function formChanged(state) {
        if (state.submitting) return;
        state.touched = true;
        state.draftDisabled = false;
        state.draft = null;
        hideDraftBanner(state);
        updateDirty(state);
        scheduleDraftSave(state);
    }

    function restoreDraft(state) {
        var draft = state.draft;
        if (!isValidDraft(draft)) return;
        var container = state.container;
        container.querySelector('.day-start').value = draft.startTime;
        container.querySelector('.day-end').value = draft.endTime;

        var tbody = container.querySelector('.entries-table tbody');
        var template = container.querySelector('.row-template');
        if (tbody && template) {
            tbody.querySelectorAll('tr.entries-row').forEach(function (row) { row.remove(); });
            draft.entries.forEach(function (entry, index) {
                var html = template.innerHTML.split('__i__').join(index);
                tbody.insertAdjacentHTML('beforeend', html);
                var row = tbody.lastElementChild;
                row.querySelector('.boat-select').value = entry.boatId;
                row.querySelector('.hours-input').value = entry.hours;
                row.querySelector('.work-description').value = entry.workDescription;
                row.querySelector('.inhouse-check').checked = entry.isInhouse;
                bindRow(row);
            });
            renumberRows(container);
        }

        recalc(container);
        state.touched = true;
        state.draftDisabled = false;
        updateDirty(state);
        scheduleDraftSave(state);
        hideDraftBanner(state);
    }

    function showDraftBanner(state) {
        var banner = document.createElement('div');
        banner.className = 'alert alert-info d-flex flex-wrap align-items-center gap-2 py-2 local-draft-banner';
        banner.setAttribute('role', 'status');
        banner.innerHTML = '<span class="small local-draft-message"></span>'
            + '<div class="d-flex gap-2 ms-auto">'
            + '<button type="button" class="btn btn-sm btn-primary restore-draft">Восстановить</button>'
            + '<button type="button" class="btn btn-sm btn-outline-secondary delete-draft">Удалить</button>'
            + '</div>';
        var savedAt = new Date(state.draft.savedAt);
        var suffix = isNaN(savedAt.getTime()) ? '' : ' от ' + savedAt.toLocaleString('ru-RU');
        banner.querySelector('.local-draft-message').textContent = 'Найден локальный черновик' + suffix + '.';
        banner.querySelector('.restore-draft').addEventListener('click', function () { restoreDraft(state); });
        banner.querySelector('.delete-draft').addEventListener('click', function () { deleteDraft(state); });
        state.container.insertBefore(banner, state.container.querySelector('.shift-panel'));
        state.banner = banner;
    }

    function resetSubmitting(state) {
        if (!state.submitting) return;
        state.submitButtons.forEach(function (item) {
            item.button.disabled = item.disabled;
            item.button.innerHTML = item.html;
            item.button.removeAttribute('aria-disabled');
        });
        if (state.actionOverridden) {
            if (state.originalAction === null) state.form.removeAttribute('action');
            else state.form.setAttribute('action', state.originalAction);
        }
        state.submitButtons = [];
        state.actionOverridden = false;
        state.submitting = false;
    }

    function beginSubmitting(state, submitter) {
        state.submitting = true;
        state.submitButtons = Array.from(state.form.querySelectorAll('button[type="submit"]')).map(function (button) {
            return { button: button, html: button.innerHTML, disabled: button.disabled };
        });
        if (submitter && submitter.hasAttribute('formaction')) {
            state.originalAction = state.form.getAttribute('action');
            state.form.action = submitter.formAction;
            state.actionOverridden = true;
        }
        state.submitButtons.forEach(function (item) {
            item.button.disabled = true;
            item.button.setAttribute('aria-disabled', 'true');
        });
        if (submitter) submitter.innerHTML = '<i class="bi bi-hourglass-split"></i> Сохраняем…';
    }

    function setupFormProtection(form) {
        var canEdit = form.getAttribute('data-can-edit') === 'true';
        var container = form.querySelector('.day-form');
        if (!container) return;
        var userId = form.getAttribute('data-user-id');
        var formType = form.getAttribute('data-workday-form');
        var date = form.getAttribute('data-date');
        var period = formType === 'week' ? form.getAttribute('data-week') : date;
        var key = userId && period ? draftPrefix + encodeURIComponent(userId) + '.' + formType + '.' + period : null;
        var state = {
            form: form,
            container: container,
            date: date,
            initialSnapshot: JSON.stringify(serializeForm(container)),
            indicator: container.querySelector('.day-form-dirty'),
            dirty: false,
            touched: false,
            submitting: false,
            submitButtons: [],
            actionOverridden: false,
            draftDisabled: false,
            draft: null,
            banner: null,
            group: key ? getDraftGroup(key) : null
        };
        formStates.push(state);
        stateByContainer.set(container, state);
        if (state.group) {
            state.group.states.push(state);
            var draft = state.group.data.forms[date];
            if (isValidDraft(draft)) {
                state.draft = draft;
                if (form.getAttribute('data-request-method') === 'GET'
                    && JSON.stringify(canonicalForm(draft)) === JSON.stringify(canonicalForm(serializeForm(container)))) {
                    deleteDraft(state);
                } else if (canEdit) {
                    showDraftBanner(state);
                }
            }
        }
        if (!canEdit) return;

        form.addEventListener('input', function () { formChanged(state); });
        form.addEventListener('change', function () { formChanged(state); });
        form.addEventListener('invalid', function () { resetSubmitting(state); }, true);
        form.addEventListener('submit', function (ev) {
            if (state.submitting) {
                ev.preventDefault();
                return;
            }
            if (ev.defaultPrevented) return;
            if (!form.checkValidity()) {
                ev.preventDefault();
                resetSubmitting(state);
                return;
            }
            flushDrafts();
            beginSubmitting(state, ev.submitter);
        });
    }

    function flushDrafts() {
        Object.keys(draftGroups).forEach(function (key) { saveDraftGroup(draftGroups[key]); });
    }

    function bindRow(row) {
        var remove = row.querySelector('.remove-row');
        if (remove) {
            remove.addEventListener('click', function () {
                var container = row.closest('.day-form');
                row.remove();
                renumberRows(container);
                recalc(container);
                var state = stateByContainer.get(container);
                if (state) formChanged(state);
            });
        }
        var check = row.querySelector('.inhouse-check');
        var select = row.querySelector('.boat-select');
        var description = row.querySelector('.work-description');
        function toggleInhouse() {
            if (select) select.disabled = check.checked || check.disabled;
            if (description) {
                description.placeholder = check.checked ? 'Описание необязательно' : 'Опишите работы';
                row.classList.toggle('is-inhouse', check.checked);
            }
        }
        if (check && select) {
            toggleInhouse();
            check.addEventListener('change', function () { toggleInhouse(); recalc(row.closest('.day-form')); });
        }
        row.querySelectorAll('.hours-input').forEach(function (inp) {
            inp.addEventListener('input', function () { recalc(row.closest('.day-form')); });
        });
    }

    document.addEventListener('click', function (ev) {
        var addBtn = ev.target.closest('.add-row');
        if (addBtn) {
            var container = addBtn.closest('.day-form');
            var template = container.querySelector('.row-template');
            var tbody = container.querySelector('.entries-table tbody');
            var index = tbody.querySelectorAll('tr.entries-row').length;
            var html = template.innerHTML.split('__i__').join(index);
            tbody.insertAdjacentHTML('beforeend', html);
            var newRow = tbody.lastElementChild;
            newRow.classList.add('row-enter');
            bindRow(newRow);
            recalc(container);
            var state = stateByContainer.get(container);
            if (state) formChanged(state);
            return;
        }
    });

    document.addEventListener('input', function (ev) {
        if (ev.target.classList.contains('day-start') || ev.target.classList.contains('day-end')) {
            recalc(ev.target.closest('.day-form'));
        }
    });

    window.addEventListener('beforeunload', function (ev) {
        flushDrafts();
        if (formStates.some(function (state) { return state.submitting; })) return;
        if (formStates.some(function (state) { return state.dirty; })) {
            ev.preventDefault();
            ev.returnValue = '';
        }
    });

    window.addEventListener('pagehide', flushDrafts);
    window.addEventListener('pageshow', function (ev) {
        if (ev.persisted) formStates.forEach(resetSubmitting);
    });

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.day-form').forEach(function (container) {
            container.querySelectorAll('tr.entries-row').forEach(bindRow);
            recalc(container);
        });
        document.querySelectorAll('form[data-workday-form]').forEach(setupFormProtection);

        var timeOnly = document.getElementById('time-only');
        var weekly = document.getElementById('weekly-view');
        if (timeOnly && weekly) {
            timeOnly.addEventListener('change', function () {
                if (timeOnly.checked) weekly.checked = false;
            });
            weekly.addEventListener('change', function () {
                if (weekly.checked) timeOnly.checked = false;
            });
        }
    });
})();

// Независимые визуальные улучшения; не вмешиваются в формы и пересчёт часов.
(function () {
    function initPageUi() {
        var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        if (!reduceMotion) {
            document.querySelectorAll('main.container-xl > *').forEach(function (block, index) {
                var delay = Math.min(index, 5) * 35;
                block.style.setProperty('--page-enter-delay', delay + 'ms');
            });
        }
        document.documentElement.classList.add('page-ready');

        var navbar = document.getElementById('nav');
        if (!navbar) return;

        var togglers = document.querySelectorAll('[data-bs-target="#nav"]');
        function setNavbarOpen(open) {
            navbar.classList.toggle('show', open);
            togglers.forEach(function (toggler) {
                toggler.setAttribute('aria-expanded', String(open));
            });
        }

        navbar.addEventListener('click', function (ev) {
            var link = ev.target.closest('a[href]');
            if (!link || !navbar.contains(link) || link.hasAttribute('data-bs-toggle')) return;
            if (!navbar.classList.contains('show')) return;

            setNavbarOpen(false);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initPageUi);
    } else {
        initPageUi();
    }
})();
