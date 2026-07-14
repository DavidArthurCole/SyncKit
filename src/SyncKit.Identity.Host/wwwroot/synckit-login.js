// src/SyncKit.Identity.Host/wwwroot/synckit-login.js
// Embeddable login widget, popup or inline iframe. Usage:
//   <script src="https://<identity-host>/synckit-login.js"></script>
//   SyncKitAuth.login("https://<identity-host>").then(({ code }) => { ... });
//   SyncKitAuth.loginInline("https://<identity-host>", containerEl).then(({ code }) => { ... });
// loginInline injects an iframe filling containerEl - the caller owns containerEl's size,
// position, and styling entirely; the widget never renders anything of its own outside it.
// The returned code is a short-lived, single-use token - exchange it for a resolved identity
// via a SERVER-SIDE call to POST /identity/redeem. Never send it anywhere else from page JS.
(function () {
  "use strict";

  function waitForMessage(identityHostUrl, sourceWindow) {
    return new Promise(function (resolve, reject) {
      function onMessage(event) {
        if (event.source !== sourceWindow) return;
        if (event.origin !== new URL(identityHostUrl).origin) return;
        if (event.data?.source !== "synckit-auth") return;
        window.removeEventListener("message", onMessage);
        if (event.data.error) {
          reject(new Error(event.data.error));
        } else {
          resolve({ code: event.data.code });
        }
      }
      window.addEventListener("message", onMessage);
    });
  }

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
      waitForMessage(identityHostUrl, popup).then(
        function (result) { settled = true; resolve(result); },
        function (err) { settled = true; reject(err); }
      );

      var closeCheck = setInterval(function () {
        if (popup.closed) {
          clearInterval(closeCheck);
          if (!settled) reject(new Error("popup_closed"));
        } else if (settled) {
          clearInterval(closeCheck);
        }
      }, 500);
    });
  }

  // containerEl: an element the caller has already sized/positioned/styled. Its entire content
  // is replaced with the login iframe; the caller controls everything outside it.
  function loginInline(identityHostUrl, containerEl) {
    var returnOrigin = window.location.origin;
    var startUrl = identityHostUrl.replace(/\/$/, "") + "/login/start?returnOrigin="
      + encodeURIComponent(returnOrigin) + "&mode=inline";

    var iframe = document.createElement("iframe");
    iframe.src = startUrl;
    iframe.style.width = "100%";
    iframe.style.height = "100%";
    iframe.style.border = "0";
    containerEl.replaceChildren(iframe);

    return waitForMessage(identityHostUrl, iframe.contentWindow);
  }

  window.SyncKitAuth = { login: login, loginInline: loginInline };
})();
