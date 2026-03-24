"use strict";

(function () {
    /**
     * Shared AI rewrite module.
     * Handles sending rewrite/polish/expand requests, displaying comparison views,
     * and accepting or discarding AI suggestions.
     *
     * Usage: Add data-ai-rewrite attributes to a container element:
     *   data-ai-url        - The POST endpoint URL
     *   data-ai-field      - The CSS selector for the textarea/input to rewrite
     *   data-ai-params     - JSON object of extra form params (e.g., {"projectId": 1})
     *   data-ai-label      - Optional button label (defaults to "AI Rewrite")
     */

    function getAntiForgeryToken(container) {
        var tokenInput = container.closest("form")?.querySelector("input[name='__RequestVerificationToken']");
        if (tokenInput) return tokenInput.value;
        // Fallback: find any token on the page
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

    function createComparisonView(original, suggestion, onAccept, onDiscard) {
        var panel = document.createElement("div");
        panel.className = "ai-comparison mt-3 rounded-lg border border-indigo-200 bg-indigo-50 p-4 dark:border-indigo-700 dark:bg-indigo-900/20";

        panel.innerHTML =
            '<p class="mb-2 text-xs font-semibold uppercase tracking-wide text-indigo-600 dark:text-indigo-400">AI Suggestion</p>' +
            '<div class="mb-3 max-h-48 overflow-y-auto rounded border border-gray-200 bg-white p-3 text-sm text-gray-800 whitespace-pre-wrap dark:border-gray-600 dark:bg-gray-800 dark:text-gray-200"></div>' +
            '<div class="flex gap-2">' +
            '  <button type="button" class="ai-accept-btn rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 dark:focus:ring-offset-gray-900">Accept</button>' +
            '  <button type="button" class="ai-discard-btn rounded-md border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-100 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700">Discard</button>' +
            '</div>';

        // Set suggestion text safely (not innerHTML)
        panel.querySelector(".max-h-48").textContent = suggestion;

        panel.querySelector(".ai-accept-btn").addEventListener("click", function () {
            onAccept(suggestion);
            panel.remove();
        });

        panel.querySelector(".ai-discard-btn").addEventListener("click", function () {
            onDiscard();
            panel.remove();
        });

        return panel;
    }

    function initAiRewriteButton(container) {
        var url = container.getAttribute("data-ai-url");
        var fieldSelector = container.getAttribute("data-ai-field");
        var paramsJson = container.getAttribute("data-ai-params") || "";
        var label = container.getAttribute("data-ai-label") || "AI Rewrite";
        var staticParams = container.getAttribute("data-ai-static-params") === "true";

        // If no data-ai-params attribute, check for an embedded JSON script element
        if (!paramsJson) {
            var jsonScript = container.querySelector('script[type="application/json"]');
            if (jsonScript) {
                paramsJson = jsonScript.textContent;
            } else {
                paramsJson = "{}";
            }
        }

        if (!url || !fieldSelector) return;

        var btn = document.createElement("button");
        btn.type = "button";
        btn.className = "ai-rewrite-btn inline-flex items-center gap-1.5 rounded-md border border-indigo-300 bg-white px-3 py-1.5 text-sm font-medium text-indigo-600 shadow-sm hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 dark:border-indigo-600 dark:bg-gray-800 dark:text-indigo-400 dark:hover:bg-gray-700 dark:focus:ring-offset-gray-900";
        btn.innerHTML =
            '<svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">' +
            '<path stroke-linecap="round" stroke-linejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456z" />' +
            '</svg>' +
            '<span>' + label + '</span>';

        container.appendChild(btn);

        function getField() {
            return document.querySelector(fieldSelector);
        }

        // For static-params mode, the button is always visible
        if (!staticParams) {
            function updateButtonVisibility() {
                var field = getField();
                if (!field) return;
                var value = field.value || "";
                btn.style.display = value.trim() === "" ? "none" : "";
            }

            // Monitor field for changes
            var field = getField();
            if (field) {
                field.addEventListener("input", updateButtonVisibility);
                // Also watch for Alpine.js model updates
                var observer = new MutationObserver(updateButtonVisibility);
                observer.observe(field, { attributes: true, childList: false, characterData: false });
                updateButtonVisibility();
            }
        }

        btn.addEventListener("click", function () {
            var field = getField();
            if (!field) return;

            var value = field.value || "";
            if (!staticParams && value.trim() === "") {
                showToast("The field is empty. Enter some text first.", true);
                return;
            }

            // Remove any existing comparison panel
            var existing = container.parentElement.querySelector(".ai-comparison");
            if (existing) existing.remove();

            // Show loading state
            btn.disabled = true;
            var originalHtml = btn.innerHTML;
            btn.innerHTML =
                '<svg class="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">' +
                '<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>' +
                '<path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>' +
                '</svg>' +
                '<span>Working...</span>';

            var params;
            try {
                params = JSON.parse(paramsJson);
            } catch (e) {
                params = {};
            }

            var formData = new URLSearchParams();
            formData.append("__RequestVerificationToken", getAntiForgeryToken(container));

            // For non-static mode, add the field value with the appropriate parameter name
            if (!staticParams) {
                var paramName = container.getAttribute("data-ai-field-param") || "description";
                formData.append(paramName, value);
            }

            // Add extra params
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

                    if (data.success && data.suggestion) {
                        var comparisonPanel = createComparisonView(
                            value,
                            data.suggestion,
                            function onAccept(suggestion) {
                                var f = getField();
                                if (f) {
                                    f.value = suggestion;
                                    f.dispatchEvent(new Event("input", { bubbles: true }));
                                }
                                showToast("Suggestion accepted.", false);
                            },
                            function onDiscard() {
                                // Nothing to do, panel is removed
                            }
                        );
                        // Insert comparison panel after the container
                        container.parentElement.insertBefore(comparisonPanel, container.nextSibling);
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

    // Initialize all AI rewrite buttons on the page
    function init() {
        document.querySelectorAll("[data-ai-rewrite]").forEach(initAiRewriteButton);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
