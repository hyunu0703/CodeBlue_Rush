mergeInto(LibraryManager.library, {
    // 브라우저 세션을 넘어 유지할 진행 데이터 하나를 읽고, 미존재와 접근 실패를 구분한다.
    CodeBlueProgressRead: function () {
        var result;
        try {
            var value = window.localStorage.getItem("codeblue-rush.progress.v1");
            result = value === null ? "M" : "D" + value;
        } catch (error) {
            result = "E";
        }
        var size = lengthBytesUTF8(result) + 1;
        var pointer = _malloc(size);
        stringToUTF8(result, pointer, size);
        return pointer;
    },

    // 전체 진행을 한 키로 교체하며, 용량·권한 예외는 성공 응답으로 바꾸지 않는다.
    CodeBlueProgressWrite: function (textPointer) {
        var result;
        try {
            window.localStorage.setItem("codeblue-rush.progress.v1", UTF8ToString(textPointer));
            result = "S";
        } catch (error) {
            result = "E";
        }
        var size = lengthBytesUTF8(result) + 1;
        var pointer = _malloc(size);
        stringToUTF8(result, pointer, size);
        return pointer;
    }
});
