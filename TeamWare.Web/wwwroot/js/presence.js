"use strict";

(function () {
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/presence")
        .withAutomaticReconnect()
        .build();

    connection.on("UserOnline", function (userId) {
        document.querySelectorAll('[data-presence-user="' + userId + '"]').forEach(function (el) {
            el.classList.remove("bg-gray-400");
            el.classList.add("bg-green-500");
            el.setAttribute("title", "Online");
        });
    });

    connection.on("UserOffline", function (userId) {
        document.querySelectorAll('[data-presence-user="' + userId + '"]').forEach(function (el) {
            el.classList.remove("bg-green-500");
            el.classList.add("bg-gray-400");
            el.setAttribute("title", "Offline");
        });
    });

    connection.start().catch(function (err) {
        console.error("SignalR connection error:", err.toString());
    });
})();
