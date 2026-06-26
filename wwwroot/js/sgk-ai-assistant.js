/**
 * SGK AI Asistanı — sgk-ai-assistant.js
 * Dış API kullanılmaz. Bilgi havuzu fetch('/data/sgkKnowledgeBase.json') ile yüklenir.
 * Tüm DOM referansları null-safe kontrol edilir; element yoksa hata fırlatmaz.
 */

(function () {
    'use strict';

    // -------------------------------------------------------------------------
    // Durum değişkenleri
    // -------------------------------------------------------------------------
    var knowledgeBase = [];
    var panelOpen = false;
    var welcomeShown = false;
    var isTyping = false;

    // -------------------------------------------------------------------------
    // Türkçe karakter normalizasyonu
    // -------------------------------------------------------------------------
    var TR_MAP = {
        'ç': 'c', 'ğ': 'g', 'ı': 'i', 'ö': 'o', 'ş': 's', 'ü': 'u',
        'Ç': 'c', 'Ğ': 'g', 'İ': 'i', 'Ö': 'o', 'Ş': 's', 'Ü': 'u'
    };

    function normalizeTR(text) {
        if (!text) return '';
        return text
            .toLowerCase()
            .replace(/[çğışöüÇĞİŞÖÜ]/g, function (c) { return TR_MAP[c] || c; })
            .replace(/[^\w\s]/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    // -------------------------------------------------------------------------
    // Bilgi havuzunu yükle
    // -------------------------------------------------------------------------
    function loadKnowledgeBase() {
        fetch('/data/sgkKnowledgeBase.json')
            .then(function (res) {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.json();
            })
            .then(function (data) {
                if (data && Array.isArray(data.entries)) {
                    knowledgeBase = data.entries;
                }
            })
            .catch(function () {
                knowledgeBase = [];
            });
    }

    // -------------------------------------------------------------------------
    // Eşleştirme motoru
    // -------------------------------------------------------------------------

    /**
     * Tek harfli "S", "K", "Ş" gibi sorgu parçacıklarını güvenli eşleşme
     * gerektiren ifadelerle tamamlamak için kontrol listesi.
     * Bu kısa parçacıklar tek başına eşleşme vermez; ancak daha uzun
     * bileşik ifade olarak geçerlerse (ör. "s harfi") normal akış işler.
     */
    var SINGLE_CHAR_TRAP = /^[a-zçğışöüşs]$/i;

    function scoreEntry(entry, queryNorm, queryTokens) {
        var score = 0;

        // --- title eşleşmesi (yüksek puan) ---
        var titleNorm = normalizeTR(entry.title || '');
        if (titleNorm.indexOf(queryNorm) !== -1) score += 20;

        // --- questions eşleşmesi (yüksek puan) ---
        if (Array.isArray(entry.questions)) {
            for (var i = 0; i < entry.questions.length; i++) {
                var qNorm = normalizeTR(entry.questions[i]);
                if (qNorm.indexOf(queryNorm) !== -1) {
                    score += 25;
                    break;
                }
                // Token bazlı kısmi eşleşme
                var matchCount = 0;
                for (var t = 0; t < queryTokens.length; t++) {
                    if (queryTokens[t].length > 2 && qNorm.indexOf(queryTokens[t]) !== -1) {
                        matchCount++;
                    }
                }
                if (matchCount > 0) score += matchCount * 5;
            }
        }

        // --- keywords eşleşmesi ---
        if (Array.isArray(entry.keywords)) {
            for (var k = 0; k < entry.keywords.length; k++) {
                var kwNorm = normalizeTR(entry.keywords[k]);
                // Tek harfli token kontrolü: keyword çok kısa ve sorguda tam token olarak geçiyorsa atla
                if (SINGLE_CHAR_TRAP.test(kwNorm)) continue;

                if (queryNorm.indexOf(kwNorm) !== -1) {
                    score += 8;
                } else {
                    // Token bazlı keyword eşleşmesi
                    var kwTokens = kwNorm.split(' ');
                    var kwMatch = 0;
                    for (var kt = 0; kt < kwTokens.length; kt++) {
                        if (kwTokens[kt].length > 2 && queryNorm.indexOf(kwTokens[kt]) !== -1) {
                            kwMatch++;
                        }
                    }
                    if (kwMatch === kwTokens.length && kwMatch > 0) {
                        score += 6;
                    } else if (kwMatch > 1) {
                        score += 3;
                    }
                }
            }
        }

        // --- category eşleşmesi (düşük puan) ---
        var catNorm = normalizeTR(entry.category || '');
        for (var ct = 0; ct < queryTokens.length; ct++) {
            if (queryTokens[ct].length > 3 && catNorm.indexOf(queryTokens[ct]) !== -1) {
                score += 2;
            }
        }

        return score;
    }

    function findBestMatch(userQuery) {
        var queryNorm = normalizeTR(userQuery);
        var queryTokens = queryNorm.split(' ').filter(function (t) { return t.length > 0; });

        if (!queryNorm || knowledgeBase.length === 0) return null;

        var bestScore = 0;
        var bestEntry = null;

        for (var i = 0; i < knowledgeBase.length; i++) {
            var s = scoreEntry(knowledgeBase[i], queryNorm, queryTokens);
            if (s > bestScore) {
                bestScore = s;
                bestEntry = knowledgeBase[i];
            }
        }

        // Minimum eşik: 6 puan
        return bestScore >= 6 ? bestEntry : null;
    }

    // -------------------------------------------------------------------------
    // DOM yardımcı fonksiyonları
    // -------------------------------------------------------------------------
    function el(id) {
        return document.getElementById(id);
    }

    function scrollToBottom() {
        var msgs = el('aiMessages');
        if (msgs) msgs.scrollTop = msgs.scrollHeight;
    }

    function appendMessage(text, sender, relatedEntry) {
        var msgs = el('aiMessages');
        if (!msgs) return;

        var wrapper = document.createElement('div');
        wrapper.className = 'ai-msg ai-msg--' + sender;

        if (sender === 'bot') {
            var avatar = document.createElement('img');
            avatar.src = '/img/ai-bear.png';
            avatar.className = 'ai-msg-avatar';
            avatar.alt = 'SGK AI';
            wrapper.appendChild(avatar);
        }

        var bubble = document.createElement('div');
        bubble.className = 'ai-msg-bubble';
        bubble.textContent = text;
        wrapper.appendChild(bubble);

        msgs.appendChild(wrapper);

        // İlgili sayfa butonu
        if (sender === 'bot' && relatedEntry && relatedEntry.relatedPage && relatedEntry.relatedButtonText) {
            var btnWrapper = document.createElement('div');
            btnWrapper.className = 'ai-related-wrap';

            var btn = document.createElement('a');
            btn.href = relatedEntry.relatedPage;
            btn.className = 'ai-related-btn';
            btn.textContent = relatedEntry.relatedButtonText;
            btn.setAttribute('aria-label', relatedEntry.relatedButtonText);

            btnWrapper.appendChild(btn);
            msgs.appendChild(btnWrapper);
        }

        scrollToBottom();
    }

    function showTypingIndicator() {
        var msgs = el('aiMessages');
        if (!msgs || isTyping) return;
        isTyping = true;

        var indicator = document.createElement('div');
        indicator.className = 'ai-msg ai-msg--bot ai-typing-indicator';
        indicator.id = 'aiTypingIndicator';

        var avatar = document.createElement('img');
        avatar.src = '/img/ai-bear.png';
        avatar.className = 'ai-msg-avatar';
        avatar.alt = '';

        var bubble = document.createElement('div');
        bubble.className = 'ai-msg-bubble ai-typing-bubble';
        bubble.innerHTML = '<span></span><span></span><span></span>';

        indicator.appendChild(avatar);
        indicator.appendChild(bubble);
        msgs.appendChild(indicator);
        scrollToBottom();
    }

    function hideTypingIndicator() {
        var indicator = el('aiTypingIndicator');
        if (indicator) indicator.parentNode.removeChild(indicator);
        isTyping = false;
    }

    // -------------------------------------------------------------------------
    // Panel kontrolü
    // -------------------------------------------------------------------------
    function openPanel() {
        var panel = el('aiPanel');
        var fab = el('aiFab');
        var bubble = el('aiBubble');
        if (!panel) return;

        panelOpen = true;
        panel.classList.add('ai-panel--open');
        if (fab) fab.setAttribute('aria-expanded', 'true');
        if (bubble) bubble.style.display = 'none';

        // İlk açılışta hoşgeldin mesajı
        if (!welcomeShown) {
            welcomeShown = true;
            setTimeout(function () {
                appendMessage(
                    'Merhaba! Ben SGK AI Asistanı\'yım. Prim, emeklilik, borçlanma ve hizmet birleştirme konularında soru sorabilirsiniz.',
                    'bot',
                    null
                );
            }, 300);
        }

        // Input'a odaklan
        setTimeout(function () {
            var input = el('aiInput');
            if (input) input.focus();
        }, 350);
    }

    function closePanel() {
        var panel = el('aiPanel');
        var fab = el('aiFab');
        if (!panel) return;

        panelOpen = false;
        panel.classList.remove('ai-panel--open');
        if (fab) fab.setAttribute('aria-expanded', 'false');
    }

    function togglePanel() {
        if (panelOpen) {
            closePanel();
        } else {
            openPanel();
        }
    }

    // -------------------------------------------------------------------------
    // Mesaj gönderme
    // -------------------------------------------------------------------------
    function sendMessage() {
        var input = el('aiInput');
        if (!input) return;

        var text = input.value.trim();
        if (!text) return;

        input.value = '';
        appendMessage(text, 'user', null);
        showTypingIndicator();

        // Kısa gecikme: gerçekçilik hissi
        setTimeout(function () {
            hideTypingIndicator();

            var match = findBestMatch(text);
            if (match) {
                appendMessage(match.answer, 'bot', match);
            } else {
                appendMessage(
                    'Bu konuda uygun bir bilgi bulunamadı. PEK, prim günü, borçlanma, EYT veya hizmet birleştirme gibi daha belirgin anahtar kelimelerle tekrar sorabilirsiniz.',
                    'bot',
                    null
                );
            }
        }, 700);
    }

    function sendQuickQuestion(text) {
        var input = el('aiInput');
        if (input) input.value = text;
        sendMessage();
    }

    // -------------------------------------------------------------------------
    // Global erişim noktaları (onclick attribute'ları için)
    // -------------------------------------------------------------------------
    window.sgkAiTogglePanel = togglePanel;
    window.sgkAiClosePanel = closePanel;
    window.sgkAiSendMessage = sendMessage;
    window.sgkAiSendQuick = sendQuickQuestion;

    // -------------------------------------------------------------------------
    // Başlatma
    // -------------------------------------------------------------------------
    function init() {
        loadKnowledgeBase();

        // Enter tuşu — onclick attribute olmayan tek etkileşim
        var input = el('aiInput');
        if (input) {
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendMessage();
                }
            });
        }

        // Panel dışına tıklayınca kapansın
        document.addEventListener('click', function (e) {
            if (!panelOpen) return;
            var panel = el('aiPanel');
            var fabEl = el('aiFab');
            if (!panel || !fabEl) return;
            if (!panel.contains(e.target) && !fabEl.contains(e.target)) {
                closePanel();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
