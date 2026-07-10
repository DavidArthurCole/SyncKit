// src/SyncKit.Identity.Host/wwwroot/synckit-login.js
// Embeddable popup-based login widget. Usage:
//   <script src="https://<identity-host>/synckit-login.js"></script>
//   SyncKitAuth.login("https://<identity-host>").then(({ code }) => { ... });
// The returned code is a short-lived, single-use token - exchange it for a resolved identity
// via a SERVER-SIDE call to POST /identity/redeem. Never send it anywhere else from page JS.
(function () {
  "use strict";

  function login(identityHostUrl) {
    return new Promise(function (resolve, reject) {
      var returnOrigin = window.location.origin;
      var startUrl = identityHostUrl.replace(/\/$/, "") + "/login/start?returnOrigin=" + encodeURIComponent(returnOrigin);
      var popup = window.open(startUrl, "synckit-login", "width=480,height=640");

      if (!popup) {
        reject(new Error("popup_blocked"));
        return;
      }

      var settled = false;

      function cleanup() {
        window.removeEventListener("message", onMessage);
        clearInterval(closeCheck);
      }

      function onMessage(event) {
        if (event.source !== popup) return;
        if (event.origin !== new URL(identityHostUrl).origin) return;
        if (event.data?.source !== "synckit-auth") return;
        settled = true;
        cleanup();
        if (event.data.error) {
          reject(new Error(event.data.error));
        } else {
          resolve({ code: event.data.code });
        }
      }

      var closeCheck = setInterval(function () {
        if (popup.closed && !settled) {
          cleanup();
          reject(new Error("popup_closed"));
        }
      }, 500);

      window.addEventListener("message", onMessage);
    });
  }

  window.SyncKitAuth = { login: login };
})();
