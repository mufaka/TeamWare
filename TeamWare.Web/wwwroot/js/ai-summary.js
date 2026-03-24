"use strict";

(function () {
    /**
     * Shared AI summary module.
     * Handles sending summary requests, displaying loading state,
     * rendering dismissible summary panels with copy-to-clipboard.
     *
     * Usage: Add data-ai-summary attributes to a container element:
     *   data-ai-summary   - Marks the container for initialization
     *   data-ai-url       - The POST endpoint URL
     *   data-ai-params    - Optional JSON object of extra form params (e.g., {"projectId": 1})
     *   data-ai-label     - Optional button label (defaults to "Generate Summary")
     *   data-ai-periods   - Optional comma-separated period options (e.g., "Today,ThisWeek,ThisMonth")
     */

    function getAntiForgeryToken(container) {
        var tokenInput = container.closest("form")?.querySelector("input[name='__RequestVerificationToken']");
        if (tokenInput) return tokenInput.value;
        tokenInput = document.querySelector("input[name='__RequestVerificationToken']");
        return tokenInput ? tokenInput.value : "";
    }

    function showToast(message, isError) {
        var toast = document.createElement("div");
        toast.className = "fixed bottom-4 right-4 z-50 rounded-lg px-4 py-3 text-sm font-medium shadow-lg transition-opacity duration-300 " +
            (isError
                ? "bg-red-600 text-white"
                : "bg-green-600 text-white");
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(function () {
            toast.style.opacity = "0";
            setTimeout(function () { toast.remove(); }, 300);
        }, 4000);
    }

    function createSummaryPanel(summaryText, onDismiss) {
        var panel = document.createElement("div");
        panel.className = "ai-summary-panel mt-4 rounded-lg border border-indigo-200 bg-indigo-50 p-4 dark:border-indigo-700 dark:bg-indigo-900/20";

        panel.innerHTML =
            '<div class="mb-3 flex items-center justify-between">' +
            '  <p class="text-xs font-semibold uppercase tracking-wide text-indigo-600 dark:text-indigo-400">AI Summary</p>' +
            '  <div class="flex gap-2">' +
            '    <button type="button" class="ai-copy-btn inline-flex items-center gap-1 rounded-md border border-gray-300 px-2.5 py-1 text-xs font-medium text-gray-700 hover:bg-gray-100 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700">' +
            '      <svg class="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">' +
            '        <path stroke-linecap="round" stroke-linejoin="round" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />' +
            '      </svg>' +
            '      Copy' +
            '    </button>' +
            '    <button type="button" class="ai-dismiss-btn inline-flex items-center rounded-md border border-gray-300 px-2.5 py-1 text-xs font-medium text-gray-700 hover:bg-gray-100 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700">' +
            '      Dismiss' +
            '    </button>' +
            '  </div>' +
            '</div>' +
            '<div class="ai-summary-content max-h-64 overflow-y-auto rounded border border-gray-200 bg-white p-3 text-sm text-gray-800 whitespace-pre-wrap dark:border-gray-600 dark:bg-gray-800 dark:text-gray-200"></div>';

        panel.querySelector(".ai-summary-content").textContent = summaryText;

        panel.querySelector(".ai-copy-btn").addEventListener("click", function () {
            navigator.clipboard.writeText(summaryText).then(function () {
                showToast("Summary copied to clipboard.", false);
            }).catch(function () {
                showToast("Failed to copy to clipboard.", true);
            });
        });

        panel.querySelector(".ai-dismiss-btn").addEventListener("click", function () {
            onDismiss();
            panel.remove();
        });

        return panel;
    }

    function initAiSummaryButton(container) {
        var url = container.getAttribute("data-ai-url");
        var paramsJson = container.getAttribute("data-ai-params") || "{}";
        var label = container.getAttribute("data-ai-label") || "Generate Summary";
        var periodsAttr = container.getAttribute("data-ai-periods");

        if (!url) return;

        var wrapper = document.createElement("div");
        wrapper.className = "flex flex-wrap items-center gap-2";

        // Period selector (if periods are specified)
        var selectedPeriod = null;
        if (periodsAttr) {
            var periods = periodsAttr.split(",");
            var periodLabels = {
                "Today": "Today",
                "ThisWeek": "This Week",
                "ThisMonth": "This Month"
            };
            selectedPeriod = periods[0];

            var periodGroup = document.createElement("div");
            periodGroup.className = "inline-flex rounded-md shadow-sm";

            periods.forEach(function (period, index) {
                var periodBtn = document.createElement("button");
                periodBtn.type = "button";
                periodBtn.textContent = periodLabels[period] || period;
                periodBtn.setAttribute("data-period", period);

                var baseClasses = "px-3 py-1.5 text-sm font-medium focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:z-10";
                var positionClasses = index === 0
                    ? "rounded-l-md border"
                    : index === periods.length - 1
                        ? "rounded-r-md border-y border-r"
                        : "border-y border-r";

                periodBtn.className = baseClasses + " " + positionClasses;

                function updatePeriodStyles() {
                    periodGroup.querySelectorAll("button").forEach(function (b) {
                        if (b.getAttribute("data-period") === selectedPeriod) {
                            b.className = b.className.replace(/border-gray-300 bg-white text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700/g, "")
                                .replace(/bg-indigo-600 text-white border-indigo-600 dark:bg-indigo-700 dark:border-indigo-700/g, "");
                            b.className += " bg-indigo-600 text-white border-indigo-600 dark:bg-indigo-700 dark:border-indigo-700";
                        } else {
                            b.className = b.className.replace(/bg-indigo-600 text-white border-indigo-600 dark:bg-indigo-700 dark:border-indigo-700/g, "")
                                .replace(/border-gray-300 bg-white text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700/g, "");
                            b.className += " border-gray-300 bg-white text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700";
                        }
                    });
                }

                periodBtn.addEventListener("click", function () {
                    selectedPeriod = period;
                    updatePeriodStyles();
                });

                periodGroup.appendChild(periodBtn);

                // Set initial styles after adding to DOM
                if (index === periods.length - 1) {
                    setTimeout(updatePeriodStyles, 0);
                }
            });

            wrapper.appendChild(periodGroup);
        }

        // Generate button
        var btn = document.createElement("button");
        btn.type = "button";
        btn.className = "ai-summary-btn inline-flex items-center gap-1.5 rounded-md border border-indigo-300 bg-white px-3 py-1.5 text-sm font-medium text-indigo-600 shadow-sm hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 dark:border-indigo-600 dark:bg-gray-800 dark:text-indigo-400 dark:hover:bg-gray-700 dark:focus:ring-offset-gray-900";
        btn.innerHTML =
            '<svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">' +
            '<path stroke-linecap="round" stroke-linejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456z" />' +
            '</svg>' +
            '<span>' + label + '</span>';
        wrapper.appendChild(btn);

        container.appendChild(wrapper);

        btn.addEventListener("click", function () {
            // Remove any existing summary panel
            var existing = container.querySelector(".ai-summary-panel");
            if (existing) existing.remove();

            // Show loading state
            btn.disabled = true;
            var originalHtml = btn.innerHTML;
            btn.innerHTML =
                '<svg class="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">' +
                '<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>' +
                '<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>' +
                '</svg>' +
                '<span>Generating...</span>';

            var params;
            try {
                params = JSON.parse(paramsJson);
            } catch (e) {
                params = {};
            }

            // Add period if applicable
            if (selectedPeriod) {
                params.period = selectedPeriod;
            }

            var formData = new URLSearchParams();
            formData.append("__RequestVerificationToken", getAntiForgeryToken(container));
            for (var key in params) {
                if (params.hasOwnProperty(key)) {
                    formData.append(key, params[key]);
                }
            }

            fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: formData.toString()
            })
                .then(function (response) { return response.json(); })
                .then(function (data) {
                    btn.disabled = false;
                    btn.innerHTML = originalHtml;

                    if (data.success && data.summary) {
                        var summaryPanel = createSummaryPanel(
                            data.summary,
                            function onDismiss() {
                                // Nothing extra needed
                            }
                        );
                        container.appendChild(summaryPanel);
                    } else {
                        showToast(data.error || "AI request failed. Please try again.", true);
                    }
                })
                .catch(function (err) {
                    btn.disabled = false;
                    btn.innerHTML = originalHtml;
                    showToast("AI request failed. Please try again.", true);
                });
        });
    }

    // Initialize all AI summary buttons on the page
    function init() {
        document.querySelectorAll("[data-ai-summary]").forEach(initAiSummaryButton);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
