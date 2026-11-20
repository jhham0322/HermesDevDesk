# AI 챗봇 v1.0.7 UI 연동 가이드

기존 채팅 연동은 `review_type`을 생략합니다. 보안성 검토 탭은 `review_type: "security"`를 명시합니다. 현재 서버에서 두 호출은 같은 보안성 검토 채팅으로 연결되며, 같은 사용자라면 대화 목록과 진행 중인 방을 공유합니다. 별도 검토 유형인 **보안증적 검토 (가상)**는 `"evidence"`이며 로컬 데모에서 사용할 수 있습니다.

## 호출 함수

아래 코드를 챗봇과 같은 오리진에서 실행하세요. Windows 로컬 확인은 `.\run-local-demo.ps1`이 출력하는 주소를 사용합니다. 

| 용도 | URI |
| --- | --- |
| 채팅도우미 화면 | `GET /chat2` · `http://127.0.0.1:8080/chat2` |
| 보안성 검토 화면 | `GET /chat1` · `http://127.0.0.1:8080/chat1` |
| UI에서 호출하는 API | `POST /chat/start` 등 아래 함수 참고 |

위 전체 주소는 기본 포트 예시입니다. 데모는 콘솔에 출력된 포트를, 회사 환경은 해당 챗봇 서버의 오리진을 사용하세요. 두 화면은 이름이 다르며, 현재 동일한 채팅 API와 대화 데이터를 사용합니다.

모든 API는 JSON을 받는 `POST /chat/*`입니다. `token`에는 로그인한 사용자에게 발급된 bearer 토큰을 전달합니다. 로컬 데모에서는 화면에 미리 채워진 테스트 토큰을 사용할 수 있습니다.

```js
function createChatClient(token, reviewType) {
    async function post(action, data = {}) {
        const body = reviewType ? { ...data, review_type: reviewType } : data;
        const response = await fetch(`/chat/${action}`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                Authorization: `Bearer ${token}`,
            },
            body: JSON.stringify(body),
        });
        const result = await response.json();
        if (!response.ok) {
            const error = new Error(result.error || "채팅 요청에 실패했습니다.");
            error.status = response.status;
            throw error;
        }
        return result;
    }

    return {
        start: () => post("start"),
        list: (before = "", limit = 30) =>
            post("list", { before_room_index: before, limit }),
        history: (room, afterSeq = 0, limit = 50) =>
            post("history", { room_index: room, afterSeq, limit }),
        select: (room, afterSeq = 0, limit = 50) =>
            post("select", { room_index: room, afterSeq, limit }),
        resume: (room, afterSeq = 0, limit = 50) =>
            post("continue", { room_index: room, status: "R", afterSeq, limit }),
        newRoom: (room = "") =>
            post("continue", { room_index: room, status: "N" }),
        send: (room, message, requestId) =>
            post("send", { room_index: room, type: "S", message, chat_index: requestId }),
        complete: room => post("end", { room_index: room, type: "C" }),
        cancel: room => post("end", { room_index: room, type: "X" }),
    };
}
```

## 기존 채팅 열기

```js
const existingChat = createChatClient(token);
const { room_index: activeRoom, has_open_chat } = await existingChat.start();
const { rooms, selected_room_index: selectedRoom } = await existingChat.list();

// 마지막으로 선택한 대화가 있으면 읽기 전용으로 복원합니다.
const selectedHistory = selectedRoom ? await existingChat.history(selectedRoom) : null;

// 사용자가 ‘이어서 하기’를 누를 때 호출합니다.
async function onResumeClick() {
    if (has_open_chat === "T") return existingChat.resume(activeRoom);
}
```

## 보안성 검토 채팅 시작 및 완료

```js
const securityChat = createChatClient(token, "security");
const securityState = await securityChat.start();

// 사용자가 ‘새 채팅’을 확정한 후 호출합니다.
// 진행 중인 방이 있으면 해당 방을 닫고 새 방을 엽니다.
const { room_index: reviewRoom } = await securityChat.newRoom(securityState.room_index);

const requestId = crypto.randomUUID();
const message = "사내 문서 검색 기능의 보안성 검토를 요청합니다.";
const reply = await securityChat.send(reviewRoom, message, requestId);
console.log(reply.message, reply.references);

// 사용자가 ‘작업 완료’를 누를 때 호출합니다.
const { draft } = await securityChat.complete(reviewRoom);
console.log(draft?.title, draft?.description);
```

## 화면 동작과 응답

| 호출 | UI에서 사용하는 결과 |
| --- | --- |
| `start()` | `has_open_chat`이 `"T"`이면 이어서 하기 버튼을 표시합니다. 진행 중인 방이 없으면 `room_index`는 빈 문자열입니다. |
| `list()` | `rooms`로 목록을 표시합니다. 다음 페이지는 마지막 항목의 `room_index`를 넘깁니다. |
| `select(room)`, `history(room)` | `messages`를 읽기 전용으로 표시합니다. `select`는 마지막 선택을 저장합니다. |
| `resume(room)`, `newRoom(room)` | 성공하면 응답의 `room_index`를 메시지 전송에 사용합니다. `resume`은 첫 메시지 페이지도 반환합니다. |
| `send(room, message, requestId)` | `message`에 AI 답변이 들어 있습니다. 같은 메시지를 재시도할 때는 같은 `requestId`를 사용합니다. |
| `complete(room)`, `cancel(room)` | 성공하면 입력을 닫고 방 목록을 갱신합니다. 완료 응답의 `draft`에서 보안성 검토 신청 초안을 읽습니다. |

이력 응답의 `messages`에서 `isbot === "Y"`는 AI 메시지입니다. 페이지가 요청한 개수만큼 찼으면 마지막 메시지의 `chat_index`를 숫자로 변환해 다음 `history(room, afterSeq, limit)`에 전달합니다. 목록에서 다른 방을 눌렀을 때는 `select`를 호출하고, 진행 중인 방에 쓰기를 시작할 때는 사용자가 이어서 하기를 선택하도록 합니다.

`401`은 인증이 필요함을, `404`는 방이 없거나 해당 사용자에게 표시할 수 없음을 나타냅니다. `409`는 현재 대화 상태나 요청 내용이 충돌한 경우입니다. 화면에 응답의 `error` 메시지를 표시하고 필요하면 `start()`와 `list()`로 상태를 다시 읽습니다.
