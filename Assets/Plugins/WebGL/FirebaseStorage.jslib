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
    }
});
