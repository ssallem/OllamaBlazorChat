
window.scrollToBottom = () => {
    const container = document.getElementById("chat-container");
    if (container) {
        container.scrollTop = container.scrollHeight + 1000;
    }
};

//window.copyToClipboard = (text) => {
//    navigator.clipboard.writeText(text).then(() => {
//        alert("메시지가 복사되었습니다!");
//    });
//};

window.copyToClipboard = (text) => {
    navigator.clipboard.writeText(text).then(() => {
        const toast = document.getElementById("toast");
        if (toast) {
            toast.style.opacity = "1";
            toast.innerText = "📋 메시지가 복사되었습니다!";
            setTimeout(() => {
                toast.style.opacity = "0";
            }, 2000);
        }
    });
};

window.typewriterEffect = async (id, fullText) => {
    const el = document.getElementById(id);
    if (!el) return;
    el.innerText = '';
    for (let i = 0; i < fullText.length; i++) {
        el.innerText += fullText[i];
        await new Promise(r => setTimeout(r, 20));
    }
};

window.toggleTheme = () => {
    const body = document.body;
    if (body.classList.contains("light")) {
        body.classList.remove("light");
        localStorage.setItem("theme", "dark");
    } else {
        body.classList.add("light");
        localStorage.setItem("theme", "light");
    }
};

window.applySavedTheme = () => {
    const saved = localStorage.getItem("theme");
    if (saved === "light") {
        document.body.classList.add("light");
    }
};