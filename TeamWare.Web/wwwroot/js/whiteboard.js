"use strict";

(function () {
    document.addEventListener("DOMContentLoaded", function () {
        var session = document.querySelector("[data-whiteboard-session]");
        if (!session || typeof signalR === "undefined" || typeof WhiteboardCanvas === "undefined") {
            return;
        }

        var whiteboardId = parseInt(session.getAttribute("data-whiteboard-id"), 10);
        var landingUrl = session.getAttribute("data-landing-url") || "/Whiteboard";
        var presenterNameElement = document.getElementById("whiteboard-presenter-name");
        var activeUsersList = document.getElementById("whiteboard-active-users");
        var chatMessagesContainer = document.getElementById("whiteboard-chat-messages");
        var chatForm = document.getElementById("whiteboard-chat-form");
        var chatInput = document.getElementById("whiteboard-chat-input");
        var initialCanvasElement = document.getElementById("whiteboard-initial-canvas");
        var initialActiveUsers = Array.from(document.querySelectorAll("#whiteboard-active-users [data-user-id]"));
        var activeUsers = new Map(initialActiveUsers.map(function (item) {
            return [item.getAttribute("data-user-id"), item.getAttribute("data-user-id")];
        }));
        var isOwner = session.getAttribute("data-is-owner") === "true";
        var isTemporary = session.getAttribute("data-is-temporary") === "true";
        var currentUserId = session.getAttribute("data-current-user-id");
        var isPresenter = session.getAttribute("data-is-presenter") === "true";
        var canvas = new WhiteboardCanvas(session, {
            isPresenter: isPresenter
        });

        if (initialCanvasElement) {
            canvas.deserialize(initialCanvasElement.textContent || "");
        }

        document.querySelectorAll("[data-whiteboard-mode]").forEach(function (button) {
            button.addEventListener("click", function () {
                document.querySelectorAll("[data-whiteboard-mode]").forEach(function (candidate) {
                    candidate.classList.remove("bg-indigo-600", "text-white");
                    candidate.classList.add("border", "border-gray-300", "bg-white", "text-gray-700");
                });

                button.classList.remove("border", "border-gray-300", "bg-white", "text-gray-700");
                button.classList.add("bg-indigo-600", "text-white");
                canvas.setMode(button.getAttribute("data-whiteboard-mode"));
            });
        });

        var debouncedSendUpdate = debounce(function () {
            if (!isPresenter || !connection || connection.state !== signalR.HubConnectionState.Connected) {
                return;
            }

            connection.invoke("SendCanvasUpdate", whiteboardId, canvas.serialize()).catch(function (error) {
                console.error("Failed to send canvas update.", error);
            });
        }, 200);

        session.addEventListener("whiteboard:changed", function () {
            debouncedSendUpdate();
        });

        var connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/whiteboard")
            .withAutomaticReconnect()
            .build();

        connection.on("CanvasUpdated", function (canvasData) {
            canvas.deserialize(canvasData);
        });

        connection.on("PresenterChanged", function (presenterId, presenterDisplayName) {
            session.setAttribute("data-is-presenter", presenterId === session.getAttribute("data-current-user-id") ? "true" : "false");
            isPresenter = session.getAttribute("data-is-presenter") === "true";
            canvas.setPresenterState(isPresenter);

            if (presenterNameElement) {
                presenterNameElement.textContent = presenterDisplayName || presenterId || "Unassigned";
            }

            renderActiveUsers(activeUsersList, activeUsers, {
                isOwner: isOwner,
                isTemporary: isTemporary,
                currentPresenterId: presenterId,
                currentUserId: currentUserId
            });
        });

        connection.on("UserJoined", function (userId, displayName) {
            activeUsers.set(userId, displayName || userId);
            renderActiveUsers(activeUsersList, activeUsers, {
                isOwner: isOwner,
                isTemporary: isTemporary,
                currentPresenterId: isPresenter ? currentUserId : null,
                currentUserId: currentUserId
            });
        });

        connection.on("UserLeft", function (userId) {
            activeUsers.delete(userId);
            renderActiveUsers(activeUsersList, activeUsers, {
                isOwner: isOwner,
                isTemporary: isTemporary,
                currentPresenterId: isPresenter ? currentUserId : null,
                currentUserId: currentUserId
            });
        });

        connection.on("UserRemoved", function () {
            window.location.href = landingUrl;
        });

        connection.on("BoardDeleted", function () {
            window.location.href = landingUrl;
        });

        connection.on("ChatMessageReceived", function (message) {
            appendChatMessage(chatMessagesContainer, message);
            scrollChatToBottom(chatMessagesContainer);
        });

        connection.onreconnected(function () {
            connection.invoke("JoinBoard", whiteboardId).catch(function (error) {
                console.error("Failed to rejoin whiteboard.", error);
            });
        });

        connection.start()
            .then(function () {
                return connection.invoke("JoinBoard", whiteboardId);
            })
            .catch(function (error) {
                console.error("Whiteboard SignalR connection error.", error);
            });

        if (chatForm && chatInput) {
            chatForm.addEventListener("submit", function (event) {
                event.preventDefault();

                var content = chatInput.value.trim();
                if (!content || !connection || connection.state !== signalR.HubConnectionState.Connected) {
                    return;
                }

                connection.invoke("SendChatMessage", whiteboardId, content)
                    .then(function () {
                        chatInput.value = "";
                    })
                    .catch(function (error) {
                        console.error("Failed to send whiteboard chat message.", error);
                    });
            });
        }

        window.whiteboardSession = {
            assignPresenter: function (userId) {
                if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
                    return;
                }

                connection.invoke("AssignPresenter", whiteboardId, userId).catch(function (error) {
                    console.error("Failed to assign presenter.", error);
                });
            },
            removeUser: function (userId) {
                if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
                    return;
                }

                connection.invoke("RemoveUser", whiteboardId, userId).catch(function (error) {
                    console.error("Failed to remove user.", error);
                });
            },
            reclaimPresenter: function () {
                if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
                    return;
                }

                connection.invoke("ReclaimPresenter", whiteboardId).catch(function (error) {
                    console.error("Failed to reclaim presenter.", error);
                });
            },
            saveToProject: function (projectId) {
                var associationContainer = document.getElementById("whiteboard-project-association");
                if (!associationContainer || typeof htmx === "undefined") {
                    return;
                }

                var tokenInput = associationContainer.querySelector('input[name="__RequestVerificationToken"]');
                var values = {
                    whiteboardId: whiteboardId,
                    __RequestVerificationToken: tokenInput ? tokenInput.value : ""
                };

                if (projectId) {
                    values.projectId = projectId;
                    htmx.ajax("POST", "/Whiteboard/SaveToProject", {
                        source: associationContainer,
                        target: "#whiteboard-project-association",
                        swap: "outerHTML",
                        values: values
                    });
                } else {
                    htmx.ajax("POST", "/Whiteboard/ClearProject", {
                        source: associationContainer,
                        target: "#whiteboard-project-association",
                        swap: "outerHTML",
                        values: values
                    });
                }
            }
        };

        scrollChatToBottom(chatMessagesContainer);
    });

    function renderActiveUsers(container, activeUsers, options) {
        if (!container) {
            return;
        }

        options = options || {};

        container.innerHTML = "";
        if (activeUsers.size === 0) {
            var emptyItem = document.createElement("li");
            emptyItem.className = "text-gray-500 dark:text-gray-400";
            emptyItem.textContent = "No active users yet.";
            container.appendChild(emptyItem);
            return;
        }

        Array.from(activeUsers.entries()).forEach(function (entry) {
            var item = document.createElement("li");
            item.setAttribute("data-user-id", entry[0]);
            item.className = "rounded-md border border-transparent px-3 py-2";

            var content = document.createElement("div");
            content.className = "flex items-center justify-between gap-2";

            var label = document.createElement("span");
            label.className = "text-gray-900 dark:text-gray-100";
            label.textContent = entry[1];
            content.appendChild(label);

            if (options.currentPresenterId && options.currentPresenterId === entry[0]) {
                var badge = document.createElement("span");
                badge.className = "inline-flex items-center rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-300";
                badge.textContent = "Presenter";
                content.appendChild(badge);
            }

            item.appendChild(content);

            if (options.isOwner) {
                var actions = document.createElement("div");
                actions.className = "mt-2 flex flex-wrap gap-2";

                if (options.currentPresenterId !== entry[0]) {
                    var presenterButton = document.createElement("button");
                    presenterButton.type = "button";
                    presenterButton.className = "inline-flex items-center rounded-md border border-gray-300 bg-white px-2.5 py-1 text-xs font-medium text-gray-700 shadow-sm hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300";
                    presenterButton.textContent = "Make Presenter";
                    presenterButton.addEventListener("click", function () {
                        if (window.whiteboardSession) {
                            window.whiteboardSession.assignPresenter(entry[0]);
                        }
                    });
                    actions.appendChild(presenterButton);
                }

                if (options.isTemporary && options.currentUserId !== entry[0]) {
                    var removeButton = document.createElement("button");
                    removeButton.type = "button";
                    removeButton.className = "inline-flex items-center rounded-md border border-red-300 bg-white px-2.5 py-1 text-xs font-medium text-red-700 shadow-sm hover:bg-red-50 dark:border-red-700 dark:bg-gray-700 dark:text-red-400 dark:hover:bg-red-900/20";
                    removeButton.textContent = "Remove User";
                    removeButton.addEventListener("click", function () {
                        if (window.whiteboardSession) {
                            window.whiteboardSession.removeUser(entry[0]);
                        }
                    });
                    actions.appendChild(removeButton);
                }

                if (actions.children.length > 0) {
                    item.appendChild(actions);
                }
            }

            container.appendChild(item);
        });
    }

    function debounce(callback, delay) {
        var timeoutId;
        return function () {
            var args = arguments;
            clearTimeout(timeoutId);
            timeoutId = setTimeout(function () {
                callback.apply(null, args);
            }, delay);
        };
    }

    function appendChatMessage(container, message) {
        if (!container || !message) {
            return;
        }

        if (container.textContent.indexOf("No chat messages yet.") !== -1) {
            container.innerHTML = "";
        }

        var wrapper = document.createElement("div");
        wrapper.className = "mb-3 rounded-md bg-white p-3 shadow-sm last:mb-0 dark:bg-gray-800";
        wrapper.setAttribute("data-chat-message-id", message.id || message.Id);

        var header = document.createElement("div");
        header.className = "flex items-center justify-between gap-2";

        var author = document.createElement("span");
        author.className = "font-medium text-gray-900 dark:text-gray-100";
        author.textContent = message.userDisplayName || message.UserDisplayName || message.userId || message.UserId;

        var timestamp = document.createElement("span");
        timestamp.className = "text-xs text-gray-500 dark:text-gray-400";
        timestamp.textContent = formatTimestamp(message.createdAt || message.CreatedAt);

        var body = document.createElement("p");
        body.className = "mt-1 whitespace-pre-wrap text-gray-700 dark:text-gray-300";
        body.textContent = message.content || message.Content || "";

        header.appendChild(author);
        header.appendChild(timestamp);
        wrapper.appendChild(header);
        wrapper.appendChild(body);
        container.appendChild(wrapper);
    }

    function scrollChatToBottom(container) {
        if (!container) {
            return;
        }

        container.scrollTop = container.scrollHeight;
    }

    function formatTimestamp(value) {
        if (!value) {
            return "Just now";
        }

        var date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        return date.toLocaleString([], {
            month: "short",
            day: "numeric",
            hour: "numeric",
            minute: "2-digit"
        });
    }
})();
