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
    function recalc(container) {
        var start = container.querySelector('.day-start');
        var end = container.querySelector('.day-end');
        var workedEl = container.querySelector('.worked-hours');
        var sumEl = container.querySelector('.entries-hours-sum');
        if (!start || !end || !workedEl || !sumEl) return;

        var sm = window.workTime.parseMinutes(start.value);
        var em = window.workTime.parseMinutes(end.value);
        var worked = em > sm ? (em - sm) / 60 : 0;
        workedEl.textContent = Math.round(worked * 100) / 100;

        var sum = 0;
        container.querySelectorAll('.hours-input').forEach(function (inp) {
            sum += window.workTime.parseHours(inp.value);
        });
        sum = Math.round(sum * 100) / 100;
        sumEl.textContent = sum;

        var invalid = sum > worked + 0.001;
        sumEl.classList.toggle('hours-sum-invalid', invalid);
        sumEl.classList.toggle('hours-sum-valid', !invalid && sum > 0);
        var table = container.querySelector('.entries-table');
        if (table) table.classList.toggle('table-overtime', invalid);
    }

    function bindRow(row) {
        var remove = row.querySelector('.remove-row');
        if (remove) {
            remove.addEventListener('click', function () {
                row.remove();
                recalc(row.closest('.day-form'));
            });
        }
        var check = row.querySelector('.inhouse-check');
        var select = row.querySelector('.boat-select');
        function toggleBoat() {
            if (select) select.disabled = check.checked || select.hasAttribute('data-force-disabled');
        }
        if (check && select) {
            toggleBoat();
            check.addEventListener('change', function () { toggleBoat(); recalc(row.closest('.day-form')); });
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
            bindRow(tbody.lastElementChild);
            recalc(container);
            return;
        }
    });

    document.addEventListener('input', function (ev) {
        if (ev.target.classList.contains('day-start') || ev.target.classList.contains('day-end')) {
            recalc(ev.target.closest('.day-form'));
        }
    });

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.day-form').forEach(function (container) {
            container.querySelectorAll('tr.entries-row').forEach(bindRow);
            recalc(container);
        });
    });
})();
