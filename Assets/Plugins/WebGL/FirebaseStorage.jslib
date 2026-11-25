mergeInto(LibraryManager.library, {
    // 토큰과 미전송 기록을 게임 진행 키와 분리해 보관한다.
    CodeBlueFirebaseRead: function () {
        var result;
        try {
            var value = window.localStorage.getItem("codeblue-rush.firebase.v1");
            result = value === null ? "M" : "D" + value;
        } catch (error) { result = "E"; }
        var size = lengthBytesUTF8(result) + 1;
        var pointer = _malloc(size);
        stringToUTF8(result, pointer, size);
        return pointer;
    },
    // 용량·접근 오류를 성공으로 숨기지 않는다.
    CodeBlueFirebaseWrite: function (jsonPointer) {
        var result;
        try {
            window.localStorage.setItem("codeblue-rush.firebase.v1", UTF8ToString(jsonPointer));
            result = "S";
        } catch (error) { result = "E"; }
        var size = lengthBytesUTF8(result) + 1;
        var pointer = _malloc(size);
        stringToUTF8(result, pointer, size);
        return pointer;
    },
    // 노출마다 별도 키를 사용하므로 다른 탭의 추가나 제거를 덮어쓰지 않는다.
    CodeBlueAdStore: function (scopePointer, idPointer, operation) {
        var result;
        try {
            var prefix = "codeblue-rush.adstats.v1." + UTF8ToString(scopePointer) + ".";
            var store = window.localStorage;
            var key = prefix + UTF8ToString(idPointer);
            result = "S";
            if (operation === 1) store.setItem(key, "1");
            else if (operation === 0) store.removeItem(key);
            else {
                var ids = [];
                for (var index = 0; index < store.length; index++) {
                    var stored = store.key(index);
                    if (stored && stored.indexOf(prefix) === 0) ids.push(stored.substring(prefix.length));
                }
                result += ids.join(",");
            }
        } catch (error) { result = "E"; }
        var size = lengthBytesUTF8(result) + 1;
        var pointer = _malloc(size);
        stringToUTF8(result, pointer, size);
        return pointer;
    }
});
