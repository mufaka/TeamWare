// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// --- Local time conversion ---
function convertTimeElements(root) {
    (root || document).querySelectorAll('time[datetime]').forEach(function (el) {
        var date = new Date(el.getAttribute('datetime'));
        if (isNaN(date)) return;
        var fmt = el.dataset.format || 'datetime';
        var opts = { month: 'short', day: 'numeric' };
        if (fmt === 'date' || fmt === 'datetime') opts.year = 'numeric';
        if (fmt === 'datetime' || fmt === 'short-datetime') {
            opts.hour = 'numeric';
            opts.minute = '2-digit';
        }
        el.textContent = date.toLocaleString(undefined, opts);
    });
}

document.addEventListener('DOMContentLoaded', function () { convertTimeElements(); });
document.addEventListener('htmx:afterSwap', function (evt) { convertTimeElements(evt.detail.target); });

document.addEventListener('alpine:init', () => {
    Alpine.data('markdownEditor', () => ({
        tab: 'write',
        value: '',
        init() {
            try {
                this.value = JSON.parse(this.$refs.initialValue.textContent);
            } catch (e) {
                this.value = '';
            }
        },
        bold() { this.wrapSelection('**', '**'); },
        italic() { this.wrapSelection('_', '_'); },
        heading() { this.insertLinePrefix('## '); },
        link() { this.wrapSelection('[', '](url)'); },
        bulletList() { this.insertLinePrefix('- '); },
        code() { this.wrapSelection('`', '`'); },
        wrapSelection(before, after) {
            var ta = this.$refs.textarea;
            var start = ta.selectionStart;
            var end = ta.selectionEnd;
            var selected = this.value.substring(start, end);
            var replacement = before + (selected || 'text') + after;
            this.value = this.value.substring(0, start) + replacement + this.value.substring(end);
            this.$nextTick(() => {
                ta.focus();
                if (selected) {
                    ta.setSelectionRange(start + before.length, start + before.length + selected.length);
                } else {
                    ta.setSelectionRange(start + before.length, start + before.length + 4);
                }
            });
        },
        insertLinePrefix(prefix) {
            var ta = this.$refs.textarea;
            var start = ta.selectionStart;
            var lineStart = this.value.lastIndexOf('\n', start - 1) + 1;
            this.value = this.value.substring(0, lineStart) + prefix + this.value.substring(lineStart);
            this.$nextTick(() => {
                ta.focus();
                ta.setSelectionRange(start + prefix.length, start + prefix.length);
            });
        },
        renderPreview() {
            if (typeof marked !== 'undefined' && this.value) {
                return marked.parse(this.value);
            }
            return '<p class="italic text-gray-400 dark:text-gray-500">Nothing to preview</p>';
        }
    }));
});
