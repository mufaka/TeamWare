"use strict";

(function () {
    var roomEl = document.getElementById("lounge-room");
    if (!roomEl) return;

    var projectIdStr = roomEl.dataset.projectId;
    var projectId = projectIdStr ? parseInt(projectIdStr, 10) : null;
    var lastReadId = roomEl.dataset.lastReadId ? parseInt(roomEl.dataset.lastReadId, 10) : null;
    var currentUserId = roomEl.dataset.currentUserId || "";

    var messageArea = document.getElementById("message-area");
    var messageList = document.getElementById("message-list");
    var messageInput = document.getElementById("message-input");
    var messageForm = document.getElementById("message-form");
    var btnSend = document.getElementById("btn-send");
    var btnLoadOlder = document.getElementById("btn-load-older");
    var newMessagesIndicator = document.getElementById("new-messages-indicator");
    var btnScrollToBottom = document.getElementById("btn-scroll-to-bottom");
    var btnTogglePinned = document.getElementById("btn-toggle-pinned");
    var pinnedArea = document.getElementById("pinned-messages-area");
    var btnClosePinned = document.getElementById("btn-close-pinned");
    var editModal = document.getElementById("edit-modal");
    var editMessageId = document.getElementById("edit-message-id");
    var editMessageContent = document.getElementById("edit-message-content");
    var btnEditSave = document.getElementById("btn-edit-save");
    var btnEditCancel = document.getElementById("btn-edit-cancel");
    var mentionDropdown = document.getElementById("mention-dropdown");

    var isAtBottom = true;
    var members = window.loungeMembers || [];
    var mentionActive = false;
    var mentionStart = -1;

    // --- SignalR Connection ---
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/lounge")
        .withAutomaticReconnect()
        .build();

    // --- Helpers ---
    function scrollToBottom() {
        messageArea.scrollTop = messageArea.scrollHeight;
    }

    function checkIfAtBottom() {
        var threshold = 50;
        isAtBottom = (messageArea.scrollHeight - messageArea.scrollTop - messageArea.clientHeight) < threshold;
    }

    function autoResizeMessageInput() {
        if (!messageInput || messageInput.tagName !== "TEXTAREA") return;

        var maxHeight = 160;
        messageInput.style.height = "auto";
        messageInput.style.height = Math.min(messageInput.scrollHeight, maxHeight) + "px";
        messageInput.style.overflowY = messageInput.scrollHeight > maxHeight ? "auto" : "hidden";
    }

    function formatTimestamp(dateStr) {
        var d = new Date(dateStr);
        var now = new Date();
        var hours = d.getHours().toString().padStart(2, "0");
        var mins = d.getMinutes().toString().padStart(2, "0");
        var time = hours + ":" + mins;

        if (d.toDateString() === now.toDateString()) {
            return time;
        }

        var yesterday = new Date(now);
        yesterday.setDate(yesterday.getDate() - 1);
        if (d.toDateString() === yesterday.toDateString()) {
            return "Yesterday " + time;
        }

        var months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        return months[d.getMonth()] + " " + d.getDate() + ", " + time;
    }

    function convertServerTimestamps(container) {
        var spans = (container || document).querySelectorAll(".message-timestamp[data-utc]");
        spans.forEach(function (span) {
            span.textContent = formatTimestamp(span.getAttribute("data-utc"));
        });
    }

    function getReactionDisplay(type) {
        switch (type) {
            case "thumbsup": return "\uD83D\uDC4D";
            case "heart": return "\u2764\uFE0F";
            case "laugh": return "\uD83D\uDE02";
            case "rocket": return "\uD83D\uDE80";
            case "eyes": return "\uD83D\uDC40";
            default: return type;
        }
    }

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function parseGenericAttributes(input) {
        var attrs = {};
        var attrRegex = /([a-zA-Z_:][\w:.-]*)\s*=\s*"([^"]*)"/g;
        var match;

        while ((match = attrRegex.exec(input)) !== null) {
            attrs[match[1]] = match[2];
        }

        return attrs;
    }

    function applyGenericAttributesToElement(element, textNode, allowedAttributes) {
        var match = textNode.textContent.match(/^\s*\{([^}]+)\}/);
        var applied = false;

        if (!match) return false;

        var attrs = parseGenericAttributes(match[1]);
        Object.keys(attrs).forEach(function (key) {
            if (allowedAttributes.indexOf(key) >= 0) {
                element.setAttribute(key, attrs[key]);
                applied = true;
            }
        });

        if (!applied) return false;

        textNode.textContent = textNode.textContent.substring(match[0].length);
        if (!textNode.textContent.length) {
            textNode.parentNode.removeChild(textNode);
        }

        return true;
    }

    function applyGenericAttributes(html) {
        var container = document.createElement("div");
        container.innerHTML = html;

        container.querySelectorAll("a").forEach(function (link) {
            var nextNode = link.nextSibling;
            if (nextNode && nextNode.nodeType === 3) {
                applyGenericAttributesToElement(link, nextNode, ["target", "rel", "class", "title"]);
            }
        });

        container.querySelectorAll("img").forEach(function (image) {
            var nextNode = image.nextSibling;
            if (nextNode && nextNode.nodeType === 3) {
                applyGenericAttributesToElement(image, nextNode, ["class", "width", "height", "loading", "decoding", "alt"]);
            }
        });

        return container.innerHTML;
    }

    function highlightMentions(html) {
        return html.replace(/@(\w+)/g, '<span class="font-semibold text-blue-600 dark:text-blue-400">@$1</span>');
    }

    // Configure marked for safe output (no raw HTML passthrough)
    if (typeof marked !== "undefined") {
        marked.setOptions({
            breaks: true,
            gfm: true
        });
    }

    function renderContent(text) {
        var html;
        if (typeof marked !== "undefined") {
            html = applyGenericAttributes(marked.parse(escapeHtml(text)));
        } else {
            html = escapeHtml(text);
        }
        return highlightMentions(html);
    }

    function createMessageHtml(msg) {
        var avatarHtml;
        if (msg.author && msg.author.avatarUrl) {
            avatarHtml = '<img src="' + escapeHtml(msg.author.avatarUrl) + '" alt="' + escapeHtml(msg.author.displayName) + '" class="h-8 w-8 rounded-full object-cover" />';
        } else {
            var initial = msg.author && msg.author.displayName ? msg.author.displayName[0].toUpperCase() : "?";
            avatarHtml = '<div class="flex h-8 w-8 items-center justify-center rounded-full bg-blue-100 text-sm font-medium text-blue-600 dark:bg-blue-900 dark:text-blue-300">' + escapeHtml(initial) + '</div>';
        }

        var authorName = msg.author ? escapeHtml(msg.author.displayName) : "Unknown";
        var timestamp = formatTimestamp(msg.createdAt);
        var isOwnMessage = msg.author && msg.author.id === currentUserId;

        // Build action buttons HTML
        var actionsHtml = '<div class="mt-1 hidden gap-1 group-hover:flex">';
        // Reaction buttons - always available
        var reactionTypes = ["thumbsup", "heart", "laugh", "rocket", "eyes"];
        for (var i = 0; i < reactionTypes.length; i++) {
            var rt = reactionTypes[i];
            actionsHtml += '<button type="button" class="reaction-add-btn rounded px-1.5 py-0.5 text-xs text-gray-400 hover:bg-gray-100 hover:text-gray-600 dark:hover:bg-gray-700 dark:hover:text-gray-300" ' +
                'data-message-id="' + msg.id + '" data-reaction-type="' + rt + '" title="' + rt + '">' +
                getReactionDisplay(rt) + '</button>';
        }
        // Edit and Delete buttons - only for own messages
        if (isOwnMessage) {
            actionsHtml += '<button type="button" class="btn-edit-message rounded px-1.5 py-0.5 text-xs text-gray-400 hover:bg-gray-100 hover:text-gray-600 dark:hover:bg-gray-700 dark:hover:text-gray-300" ' +
                'data-message-id="' + msg.id + '" title="Edit message">Edit</button>';
            actionsHtml += '<button type="button" class="btn-delete-message rounded px-1.5 py-0.5 text-xs text-red-400 hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-900/20 dark:hover:text-red-300" ' +
                'data-message-id="' + msg.id + '" title="Delete message">Delete</button>';
        }
        actionsHtml += '</div>';

        return '<div class="group mb-4 flex gap-3" id="message-' + msg.id + '" data-message-id="' + msg.id + '" data-author-id="' + (msg.author ? msg.author.id : "") + '" role="article" aria-label="Message from ' + authorName + '">' +
            '<div class="flex-shrink-0">' + avatarHtml + '</div>' +
            '<div class="min-w-0 flex-1">' +
                '<div class="flex items-baseline gap-2">' +
                    '<span class="text-sm font-semibold text-gray-900 dark:text-white">' + authorName + '</span>' +
                    '<span class="message-timestamp text-xs text-gray-500 dark:text-gray-400" data-utc="' + msg.createdAt + '">' + timestamp + '</span>' +
                '</div>' +
                '<div class="message-content markdown-body mt-1 text-sm text-gray-700 dark:text-gray-300" data-content="' + escapeHtml(msg.content) + '">' + renderContent(msg.content) + '</div>' +
                '<div class="mt-1 flex flex-wrap gap-1" data-reactions></div>' +
                actionsHtml +
                '<div id="task-status-' + msg.id + '"></div>' +
            '</div>' +
        '</div>';
    }

    // --- SignalR Event Handlers ---
    connection.on("ReceiveMessage", function (msg) {
        checkIfAtBottom();
        messageList.insertAdjacentHTML("beforeend", createMessageHtml(msg));

        if (isAtBottom) {
            scrollToBottom();
            // Auto mark as read
            connection.invoke("MarkAsRead", projectId, msg.id).then(function () {
                clearRoomBadge();
            }).catch(function () { });
        } else {
            newMessagesIndicator.classList.remove("hidden");
        }
    });

    connection.on("MessageEdited", function (msg) {
        var el = document.getElementById("message-" + msg.id);
        if (!el) return;
        var contentEl = el.querySelector(".message-content");
        if (contentEl) {
            contentEl.setAttribute("data-content", msg.content);
            contentEl.innerHTML = renderContent(msg.content);
        }
        // Add edited indicator if not already present
        var headerEl = el.querySelector(".flex.items-baseline");
        if (headerEl && !headerEl.querySelector(".edited-indicator")) {
            var editedSpan = document.createElement("span");
            editedSpan.className = "edited-indicator text-xs text-gray-400 dark:text-gray-500";
            editedSpan.textContent = "(edited)";
            headerEl.appendChild(editedSpan);
        }
    });

    connection.on("MessageDeleted", function (msg) {
        var el = document.getElementById("message-" + msg.id);
        if (el) {
            el.remove();
        }
    });

    connection.on("MessagePinned", function (msg) {
        var el = document.getElementById("message-" + msg.id);
        if (el) {
            var headerEl = el.querySelector(".flex.items-baseline");
            if (headerEl && !headerEl.querySelector(".pinned-indicator")) {
                var pinnedSpan = document.createElement("span");
                pinnedSpan.className = "pinned-indicator text-xs font-medium text-yellow-600 dark:text-yellow-400";
                pinnedSpan.innerHTML = '<svg class="inline-block h-3 w-3" fill="currentColor" viewBox="0 0 24 24"><path d="M5 5a2 2 0 012-2h10a2 2 0 012 2v16l-7-3.5L5 21V5z" /></svg> Pinned';
                headerEl.appendChild(pinnedSpan);
            }
        }
    });

    connection.on("MessageUnpinned", function (msg) {
        var el = document.getElementById("message-" + msg.id);
        if (el) {
            var pinnedSpan = el.querySelector(".pinned-indicator");
            if (pinnedSpan) pinnedSpan.remove();
        }
    });

    connection.on("ReactionUpdated", function (data) {
        var el = document.getElementById("message-" + data.messageId);
        if (!el) return;
        var reactionsEl = el.querySelector("[data-reactions]");
        if (!reactionsEl) return;

        // Track current user's own reaction state
        var actorIsMe = data.actorUserId === currentUserId;

        // Build a set of types the current user has reacted to.
        // Start from existing state and toggle if the current user was the actor.
        var myReactions = {};
        reactionsEl.querySelectorAll(".reaction-btn").forEach(function (btn) {
            if (btn.classList.contains("border-blue-300")) {
                myReactions[btn.dataset.reactionType] = true;
            }
        });
        if (actorIsMe && data.toggledType) {
            myReactions[data.toggledType] = !myReactions[data.toggledType];
        }

        var html = "";
        if (data.reactions) {
            data.reactions.forEach(function (r) {
                var userReacted = !!myReactions[r.reactionType];
                var activeClass = userReacted
                    ? "border-blue-300 bg-blue-50 text-blue-700 dark:border-blue-600 dark:bg-blue-900/30 dark:text-blue-300"
                    : "border-gray-200 bg-gray-50 text-gray-600 hover:bg-gray-100 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600";
                html += '<button type="button" class="reaction-btn inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs ' + activeClass + '" data-message-id="' + data.messageId + '" data-reaction-type="' + r.reactionType + '">' +
                    '<span class="reaction-emoji">' + getReactionDisplay(r.reactionType) + '</span>' +
                    '<span class="reaction-count">' + r.count + '</span>' +
                '</button>';
            });
        }
        reactionsEl.innerHTML = html;
    });

    connection.on("TaskCreatedFromMessage", function (data) {
        var statusEl = document.getElementById("task-status-" + data.messageId);
        if (statusEl) {
            statusEl.innerHTML = '<a href="/Task/Details/' + data.taskId + '" class="text-xs text-green-600 hover:text-green-700 dark:text-green-400 dark:hover:text-green-300">Task #' + data.taskId + ' created from this message</a>';
        }
    });

    // --- Clear the sidebar unread badge for the current room in real time ---
    function clearRoomBadge() {
        var roomBadgeAttr = projectId !== null ? String(projectId) : "general";
        var roomBadge = document.querySelector('[data-lounge-room-badge="' + roomBadgeAttr + '"]');
        if (!roomBadge) return;

        var badgeCount = parseInt(roomBadge.textContent.trim(), 10) || 0;
        roomBadge.remove();

        var totalBadge = document.getElementById("lounge-total-badge");
        if (totalBadge) {
            var totalCount = parseInt(totalBadge.textContent.trim(), 10) || 0;
            totalCount -= badgeCount;
            if (totalCount <= 0) {
                totalBadge.remove();
            } else {
                totalBadge.textContent = totalCount;
            }
        }
    }

    // --- Helper function to mark latest message as read ---
    function markLatestAsRead() {
        var lastMsg = messageList.querySelector("[data-message-id]:last-child");
        if (lastMsg) {
            var msgId = parseInt(lastMsg.dataset.messageId, 10);
            connection.invoke("MarkAsRead", projectId, msgId).then(function () {
                clearRoomBadge();
            }).catch(function () { });
        }
    }

    // --- Connection Start ---
    connection.start().then(function () {
        return connection.invoke("JoinRoom", projectId);
    }).then(function () {
        scrollToBottom();
        // Mark messages as read on initial load if at bottom
        setTimeout(function () {
            checkIfAtBottom();
            if (isAtBottom) {
                markLatestAsRead();
            }
        }, 100);
    }).catch(function (err) {
        console.error("SignalR connection error:", err.toString());
    });

    // --- Send Message ---
    messageForm.addEventListener("submit", function (e) {
        e.preventDefault();
        var content = messageInput.value.trim();
        if (!content) return;

        btnSend.disabled = true;
        connection.invoke("SendMessage", projectId, content).then(function () {
            messageInput.value = "";
            autoResizeMessageInput();
            btnSend.disabled = false;
            messageInput.focus();
            // Mark the sent message as read after a brief delay
            // to ensure the message is added to the DOM
            setTimeout(markLatestAsRead, 50);
        }).catch(function (err) {
            console.error("Send error:", err.toString());
            btnSend.disabled = false;
        });
    });

    // --- Scroll handling ---
    messageArea.addEventListener("scroll", function () {
        checkIfAtBottom();
        if (isAtBottom) {
            newMessagesIndicator.classList.add("hidden");
            markLatestAsRead();
        }
    });

    if (btnScrollToBottom) {
        btnScrollToBottom.addEventListener("click", function () {
            scrollToBottom();
            newMessagesIndicator.classList.add("hidden");
        });
    }

    // --- Load older messages ---
    if (btnLoadOlder) {
        btnLoadOlder.addEventListener("click", function () {
            var firstMsg = messageList.querySelector("[data-message-id]");
            if (!firstMsg) return;

            var oldestId = firstMsg.dataset.messageId;
            var oldestTimestamp = firstMsg.querySelector(".message-timestamp[data-utc]");
            // Use the ISO datetime from the data-utc attribute
            var beforeParam = oldestTimestamp ? oldestTimestamp.getAttribute("data-utc") : null;

            var url = "/Lounge/Messages?count=50";
            if (projectId !== null) url += "&projectId=" + projectId;
            if (beforeParam) {
                url += "&before=" + encodeURIComponent(beforeParam);
            }

            fetch(url, { headers: { "HX-Request": "true" } })
                .then(function (r) { return r.text(); })
                .then(function (html) {
                    if (html.trim()) {
                        var prevScrollHeight = messageArea.scrollHeight;
                        messageList.insertAdjacentHTML("afterbegin", html);
                        // Convert timestamps in newly loaded messages
                        convertServerTimestamps(messageList);
                        // Maintain scroll position
                        messageArea.scrollTop = messageArea.scrollHeight - prevScrollHeight;
                    }
                    // Check if we should hide the button (less than 50 messages returned)
                    var temp = document.createElement("div");
                    temp.innerHTML = html;
                    if (temp.querySelectorAll("[data-message-id]").length < 50) {
                        btnLoadOlder.style.display = "none";
                    }
                })
                .catch(function (err) {
                    console.error("Load older error:", err.toString());
                });
        });
    }

    // --- Pinned messages toggle ---
    if (btnTogglePinned) {
        btnTogglePinned.addEventListener("click", function () {
            pinnedArea.classList.toggle("hidden");
            if (!pinnedArea.classList.contains("hidden")) {
                convertServerTimestamps(pinnedArea);
            }
        });
    }

    if (btnClosePinned) {
        btnClosePinned.addEventListener("click", function () {
            pinnedArea.classList.add("hidden");
        });
    }

    // --- Edit message ---
    document.addEventListener("click", function (e) {
        var editBtn = e.target.closest(".btn-edit-message");
        if (editBtn) {
            var msgId = editBtn.dataset.messageId;
            var msgEl = document.getElementById("message-" + msgId);
            if (!msgEl) return;
            var contentEl = msgEl.querySelector(".message-content");
            editMessageId.value = msgId;
            editMessageContent.value = contentEl ? contentEl.getAttribute("data-content") : "";
            editModal.classList.remove("hidden");
            editModal.classList.add("flex");
            editMessageContent.focus();
        }
    });

    if (btnEditCancel) {
        btnEditCancel.addEventListener("click", function () {
            editModal.classList.add("hidden");
            editModal.classList.remove("flex");
        });
    }

    if (btnEditSave) {
        btnEditSave.addEventListener("click", function () {
            var msgId = parseInt(editMessageId.value, 10);
            var content = editMessageContent.value.trim();
            if (!content) return;

            connection.invoke("EditMessage", msgId, content).then(function () {
                editModal.classList.add("hidden");
                editModal.classList.remove("flex");
            }).catch(function (err) {
                console.error("Edit error:", err.toString());
            });
        });
    }

    // --- Delete message ---
    document.addEventListener("click", function (e) {
        var deleteBtn = e.target.closest(".btn-delete-message");
        if (deleteBtn) {
            var msgId = parseInt(deleteBtn.dataset.messageId, 10);
            if (confirm("Are you sure you want to delete this message?")) {
                connection.invoke("DeleteMessage", msgId).catch(function (err) {
                    console.error("Delete error:", err.toString());
                });
            }
        }
    });

    // --- Reaction buttons ---
    document.addEventListener("click", function (e) {
        var reactionBtn = e.target.closest(".reaction-btn, .reaction-add-btn");
        if (reactionBtn) {
            var msgId = parseInt(reactionBtn.dataset.messageId, 10);
            var reactionType = reactionBtn.dataset.reactionType;
            connection.invoke("ToggleReaction", msgId, reactionType).catch(function (err) {
                console.error("Reaction error:", err.toString());
            });
        }
    });

    // --- Attachment upload ---
    var attachmentForm = document.getElementById("attachment-upload-form");
    if (attachmentForm) {
        attachmentForm.addEventListener("submit", function () {
            var contentField = document.getElementById("attachment-content");
            if (contentField) {
                var text = messageInput.value.trim();
                contentField.value = text || "";
                // Clear the message input since the server will create the message
                messageInput.value = "";
            }
        });
    }

    // --- Mention Autocomplete (18.5) ---
    messageInput.addEventListener("input", function () {
        autoResizeMessageInput();

        var val = messageInput.value;
        var cursorPos = messageInput.selectionStart;

        // Find the last @ before cursor
        var textBeforeCursor = val.substring(0, cursorPos);
        var atIndex = textBeforeCursor.lastIndexOf("@");

        if (atIndex >= 0) {
            var charBefore = atIndex > 0 ? textBeforeCursor[atIndex - 1] : " ";
            if (charBefore === " " || charBefore === "\n" || atIndex === 0) {
                var query = textBeforeCursor.substring(atIndex + 1);
                // Only trigger if no space after the @
                if (query.indexOf(" ") === -1 && query.length > 0) {
                    mentionActive = true;
                    mentionStart = atIndex;
                    showMentionDropdown(query);
                    return;
                }
            }
        }

        hideMentionDropdown();
    });

    messageInput.addEventListener("keydown", function (e) {
        if (mentionActive) {
            var items = mentionDropdown.querySelectorAll("[data-mention-username]");
            var activeItem = mentionDropdown.querySelector(".bg-blue-100, .dark\\:bg-blue-800");

            if (e.key === "ArrowDown") {
                e.preventDefault();
                selectNextMention(items, activeItem, 1);
                return;
            }

            if (e.key === "ArrowUp") {
                e.preventDefault();
                selectNextMention(items, activeItem, -1);
                return;
            }

            if (e.key === "Enter" || e.key === "Tab") {
                if (activeItem) {
                    e.preventDefault();
                    insertMention(activeItem.dataset.mentionUsername);
                    return;
                }
            } else if (e.key === "Escape") {
                hideMentionDropdown();
                return;
            }
        }

        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            if (typeof messageForm.requestSubmit === "function") {
                messageForm.requestSubmit();
            } else {
                messageForm.dispatchEvent(new Event("submit", { cancelable: true }));
            }
        }
    });

    function showMentionDropdown(query) {
        var filtered = members.filter(function (m) {
            return m.displayName.toLowerCase().indexOf(query.toLowerCase()) >= 0 ||
                   m.userName.toLowerCase().indexOf(query.toLowerCase()) >= 0;
        }).slice(0, 10);

        if (filtered.length === 0) {
            hideMentionDropdown();
            return;
        }

        var html = "";
        filtered.forEach(function (m, i) {
            var activeClass = i === 0 ? " bg-blue-100 dark:bg-blue-800" : "";
            var ariaSelected = i === 0 ? "true" : "false";
            html += '<div class="cursor-pointer px-3 py-2 text-sm hover:bg-gray-100 dark:hover:bg-gray-600' + activeClass + '" role="option" aria-selected="' + ariaSelected + '" data-mention-username="' + escapeHtml(m.userName) + '">' +
                '<span class="font-medium text-gray-900 dark:text-white">' + escapeHtml(m.displayName) + '</span>' +
                ' <span class="text-gray-400 dark:text-gray-500">@' + escapeHtml(m.userName) + '</span>' +
            '</div>';
        });

        mentionDropdown.innerHTML = html;
        mentionDropdown.classList.remove("hidden");

        // Click handler for dropdown items
        mentionDropdown.querySelectorAll("[data-mention-username]").forEach(function (item) {
            item.addEventListener("click", function () {
                insertMention(item.dataset.mentionUsername);
            });
        });
    }

    function hideMentionDropdown() {
        mentionActive = false;
        mentionStart = -1;
        mentionDropdown.classList.add("hidden");
        mentionDropdown.innerHTML = "";
        messageInput.removeAttribute("aria-activedescendant");
    }

    function selectNextMention(items, activeItem, direction) {
        if (items.length === 0) return;

        var currentIndex = -1;
        items.forEach(function (item, i) {
            if (item === activeItem) currentIndex = i;
            item.classList.remove("bg-blue-100", "dark:bg-blue-800");
            item.setAttribute("aria-selected", "false");
        });

        var nextIndex = currentIndex + direction;
        if (nextIndex < 0) nextIndex = items.length - 1;
        if (nextIndex >= items.length) nextIndex = 0;

        items[nextIndex].classList.add("bg-blue-100", "dark:bg-blue-800");
        items[nextIndex].setAttribute("aria-selected", "true");
        items[nextIndex].scrollIntoView({ block: "nearest" });
    }

    function insertMention(username) {
        var val = messageInput.value;
        var before = val.substring(0, mentionStart);
        var after = val.substring(messageInput.selectionStart);
        messageInput.value = before + "@" + username + " " + after;
        messageInput.selectionStart = messageInput.selectionEnd = before.length + username.length + 2;
        hideMentionDropdown();
        messageInput.focus();
    }

    // Close dropdown on outside click
    document.addEventListener("click", function (e) {
        if (!mentionDropdown.contains(e.target) && e.target !== messageInput) {
            hideMentionDropdown();
        }
    });

    // --- Initial scroll and new messages divider ---
    (function () {
        autoResizeMessageInput();

        // Convert server-rendered UTC timestamps to local time
        convertServerTimestamps(messageList);
        if (lastReadId) {
            var divider = document.getElementById("new-messages-divider");
            // Find the first message after the last read
            var allMessages = messageList.querySelectorAll("[data-message-id]");
            var foundLastRead = false;
            for (var i = 0; i < allMessages.length; i++) {
                var msgId = parseInt(allMessages[i].dataset.messageId, 10);
                if (msgId === lastReadId) {
                    foundLastRead = true;
                } else if (foundLastRead && msgId > lastReadId) {
                    // Insert divider before this message
                    divider.classList.remove("hidden");
                    divider.classList.add("flex");
                    allMessages[i].parentNode.insertBefore(divider, allMessages[i]);
                    divider.scrollIntoView({ behavior: "auto", block: "center" });
                    break;
                }
            }
            if (!foundLastRead) {
                scrollToBottom();
            }
        } else {
            scrollToBottom();
        }
    })();

    // Cleanup on page navigation
    window.addEventListener("beforeunload", function () {
        if (connection.state === signalR.HubConnectionState.Connected) {
            connection.invoke("LeaveRoom", projectId).catch(function () { });
        }
    });
})();
