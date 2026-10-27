mergeInto(LibraryManager.library, {
    $CodeBlueBackground: { dispose: null },

    CodeBluePauseStart__deps: ["$CodeBlueBackground"],
    // 엔진 프레임이 중단되기 전에 탭 숨김과 창 포커스를 Unity로 즉시 전달한다.
    CodeBluePauseStart: function (ownerPointer) {
        if (CodeBlueBackground.dispose) CodeBlueBackground.dispose();
        var owner = UTF8ToString(ownerPointer);
        var pageHidden = false;
        var focused = document.hasFocus();
        var publish = function () {
            Module.SendMessage(owner, "ReceiveBrowserState",
                (document.hidden || pageHidden ? "1" : "0") + (focused ? "1" : "0"));
        };
        var visibility = function () { focused = document.hasFocus(); publish(); };
        var blur = function () { focused = false; publish(); };
        var focus = function () { focused = true; publish(); };
        var hide = function () { pageHidden = true; publish(); };
        var show = function () { pageHidden = false; focused = document.hasFocus(); publish(); };
        document.addEventListener("visibilitychange", visibility);
        window.addEventListener("blur", blur);
        window.addEventListener("focus", focus);
        window.addEventListener("pagehide", hide);
        window.addEventListener("pageshow", show);
        CodeBlueBackground.dispose = function () {
            document.removeEventListener("visibilitychange", visibility);
            window.removeEventListener("blur", blur);
            window.removeEventListener("focus", focus);
            window.removeEventListener("pagehide", hide);
            window.removeEventListener("pageshow", show);
            CodeBlueBackground.dispose = null;
        };
        publish();
    },

    CodeBluePauseStop__deps: ["$CodeBlueBackground"],
    // 재실행과 종료 시 중복 알림과 파괴된 인스턴스로의 전달을 막는다.
    CodeBluePauseStop: function () {
        if (CodeBlueBackground.dispose) CodeBlueBackground.dispose();
    }
});
