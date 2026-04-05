"use strict";

(function () {
    var taskContainer = document.querySelector("[data-task-id]");
    if (!taskContainer) return;

    var taskId = parseInt(taskContainer.dataset.taskId, 10);
    if (isNaN(taskId)) return;

    // Section element → partial URL mapping
    var sectionMap = {};
    var sectionIds = ["task-status-section", "task-change-status-section", "comments-section", "task-activity-section"];
    for (var i = 0; i < sectionIds.length; i++) {
        var el = document.getElementById(sectionIds[i]);
        if (el && el.dataset.partialUrl) {
            sectionMap[sectionIds[i]] = { element: el, url: el.dataset.partialUrl };
        }
    }

    // Map section names from TaskUpdated payload to element IDs
    // A single section name can map to multiple element IDs
    var sectionNameToIds = {
        "status": ["task-status-section", "task-change-status-section"],
        "comments": ["comments-section"],
        "activity": ["task-activity-section"]
    };

    // --- Toast Notifications ---
    var toastContainer = document.getElementById("task-toast-container");

    function showToast(summary) {
        if (!toastContainer || !summary) return;

        var toast = document.createElement("div");
        toast.className = "flex items-center justify-between rounded-md bg-blue-50 px-4 py-3 text-sm text-blue-800 shadow-lg dark:bg-blue-900/30 dark:text-blue-300";
        toast.setAttribute("role", "alert");
        toast.style.opacity = "0";
        toast.style.transition = "opacity 300ms ease-in";

        var textSpan = document.createElement("span");
        textSpan.textContent = summary;
        toast.appendChild(textSpan);

        var closeBtn = document.createElement("button");
        closeBtn.innerHTML = "&times;";
        closeBtn.className = "ml-4 text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-200";
        closeBtn.setAttribute("aria-label", "Dismiss");
        closeBtn.addEventListener("click", function () {
            removeToast(toast);
        });
        toast.appendChild(closeBtn);

        toastContainer.appendChild(toast);

        // Trigger fade-in on next frame
        requestAnimationFrame(function () {
            toast.style.opacity = "1";
        });

        // Auto-dismiss after 5 seconds
        setTimeout(function () {
            removeToast(toast);
        }, 5000);
    }

    function removeToast(toast) {
        if (!toast.parentNode) return;
        toast.style.opacity = "0";
        setTimeout(function () {
            if (toast.parentNode) {
                toast.parentNode.removeChild(toast);
            }
        }, 300);
    }

    // --- Debounce ---
    var pendingSections = {};
    var debounceTimer = null;
    var DEBOUNCE_MS = 200;

    function queueSectionRefresh(sections, summary) {
        for (var i = 0; i < sections.length; i++) {
            pendingSections[sections[i]] = true;
        }

        if (summary) {
            // Show toast immediately (not debounced)
            showToast(summary);
        }

        if (debounceTimer) {
            clearTimeout(debounceTimer);
        }

        debounceTimer = setTimeout(function () {
            flushPendingSections();
        }, DEBOUNCE_MS);
    }

    function flushPendingSections() {
        var sections = Object.keys(pendingSections);
        pendingSections = {};
        debounceTimer = null;

        for (var i = 0; i < sections.length; i++) {
            var sectionName = sections[i];
            var elIds = sectionNameToIds[sectionName];
            if (!elIds) continue;

            for (var j = 0; j < elIds.length; j++) {
                var info = sectionMap[elIds[j]];
                if (!info) continue;

                // Use htmx if available, otherwise fall back to fetch
                if (typeof htmx !== "undefined") {
                    htmx.ajax("GET", info.url, { target: info.element, swap: "innerHTML" });
                } else {
                    (function (target, url) {
                        fetch(url, { credentials: "same-origin" })
                            .then(function (r) { return r.text(); })
                            .then(function (html) { target.innerHTML = html; })
                            .catch(function (err) { console.error("Task realtime fetch error:", err); });
                    })(info.element, info.url);
                }
            }
        }
    }

    // --- SignalR Connection ---
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/task")
        .withAutomaticReconnect()
        .build();

    connection.on("TaskUpdated", function (data) {
        if (data.taskId !== taskId) return;
        queueSectionRefresh(data.sections || [], data.summary || "");
    });

    connection.onreconnected(function () {
        connection.invoke("JoinTask", taskId).catch(function (err) {
            console.error("TaskHub re-join error:", err.toString());
        });
    });

    connection.start().then(function () {
        return connection.invoke("JoinTask", taskId);
    }).catch(function (err) {
        console.error("TaskHub connection error:", err.toString());
    });

    // Cleanup on page navigation
    window.addEventListener("beforeunload", function () {
        if (connection.state === signalR.HubConnectionState.Connected) {
            connection.invoke("LeaveTask", taskId).catch(function () { });
        }
    });
})();
