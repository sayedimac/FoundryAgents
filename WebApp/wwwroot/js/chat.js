// GitHub-styled Chat Application
// SignalR client for real-time AI chat

class ChatApp {
    constructor() {
        this.connection = null;
        this.sessionId = null;
        this.currentAgent = 'Writer';
        this.attachedFiles = [];
        this.currentMessageId = null;
        this.currentMessageContent = '';
        this.isConnected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;

        this.init();
    }

    async init() {
        this.loadTheme();
        await this.setupSignalR();
        this.setupEventListeners();
        this.setupMarkdown();
    }

    loadTheme() {
        const savedTheme = localStorage.getItem('chat-theme') || 'light';
        document.getElementById('chatContainer').dataset.theme = savedTheme;
    }

    async setupSignalR() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl('/chathub')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // Connection state handlers
        this.connection.onreconnecting(error => {
            console.log('Reconnecting...', error);
            this.showConnectionStatus('Reconnecting...');
        });

        this.connection.onreconnected(connectionId => {
            console.log('Reconnected:', connectionId);
            this.hideConnectionStatus();
            this.isConnected = true;
        });

        this.connection.onclose(error => {
            console.log('Connection closed:', error);
            this.isConnected = false;
            this.showConnectionStatus('Disconnected. Refresh to reconnect.');
        });

        // Message handlers
        this.connection.on('ReceiveMessageChunk', (messageId, chunk) => {
            this.handleMessageChunk(messageId, chunk);
        });

        this.connection.on('MessageComplete', (messageId) => {
            this.handleMessageComplete(messageId);
        });

        this.connection.on('ReceiveToolCall', (messageId, toolName, args) => {
            this.showToolNotification(toolName, 'Running...');
        });

        this.connection.on('ReceiveToolResult', (messageId, toolName, result) => {
            this.showToolNotification(toolName, 'Complete');
            setTimeout(() => this.hideToolNotification(), 2000);
        });

        this.connection.on('ReceiveError', (messageId, error) => {
            this.hideTypingIndicator();
            this.addSystemMessage(`Error: ${error}`, 'error');
        });

        this.connection.on('AgentSelected', (agentInfo) => {
            this.updateAgentDisplay(agentInfo);
        });

        this.connection.on('SessionCreated', (sessionId) => {
            this.sessionId = sessionId;
            console.log('Session created:', sessionId);
        });

        this.connection.on('SessionCleared', (sessionId) => {
            this.clearMessageList();
            this.showWelcomeMessage();
        });

        try {
            await this.connection.start();
            console.log('SignalR connected');
            this.isConnected = true;
            await this.createSession();
            await this.loadAgents();
        } catch (error) {
            console.error('SignalR connection failed:', error);
            this.showConnectionStatus('Connection failed. Check console for details.');
        }
    }

    setupEventListeners() {
        // Form submission
        document.getElementById('chatForm').addEventListener('submit', (e) => {
            e.preventDefault();
            this.sendMessage();
        });

        // Agent selection
        document.getElementById('agentSelect').addEventListener('change', (e) => {
            this.selectAgent(e.target.value);
        });

        // Theme toggle
        document.getElementById('themeToggle').addEventListener('click', () => {
            this.toggleTheme();
        });

        // New chat button
        document.getElementById('newChatBtn').addEventListener('click', () => {
            this.createNewSession();
        });

        // Clear chat button
        document.getElementById('clearChatBtn').addEventListener('click', () => {
            this.clearSession();
        });

        // File attachment
        document.getElementById('attachBtn').addEventListener('click', () => {
            document.getElementById('fileInput').click();
        });

        document.getElementById('fileInput').addEventListener('change', (e) => {
            this.handleFileSelect(e.target.files);
        });

        // Auto-resize textarea
        const textarea = document.getElementById('messageInput');
        textarea.addEventListener('input', () => {
            textarea.style.height = 'auto';
            textarea.style.height = Math.min(textarea.scrollHeight, 200) + 'px';
        });

        // Enter to send, Shift+Enter for new line
        textarea.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });
    }

    setupMarkdown() {
        marked.setOptions({
            breaks: true,
            gfm: true,
            highlight: (code, lang) => {
                if (lang && hljs.getLanguage(lang)) {
                    try {
                        return hljs.highlight(code, { language: lang }).value;
                    } catch (e) {
                        console.error('Highlight error:', e);
                    }
                }
                return hljs.highlightAuto(code).value;
            }
        });
    }

    async sendMessage() {
        const input = document.getElementById('messageInput');
        const message = input.value.trim();

        if (!message || !this.isConnected) return;

        // Clear input and reset height
        input.value = '';
        input.style.height = 'auto';

        // Hide welcome message if visible
        this.hideWelcomeMessage();

        // Add user message to UI
        this.addMessageToUI('user', message);

        // Show typing indicator
        this.showTypingIndicator();

        try {
            await this.connection.invoke('SendMessage', this.sessionId, this.currentAgent, message);
        } catch (error) {
            console.error('Send message error:', error);
            this.hideTypingIndicator();
            this.addSystemMessage(`Failed to send message: ${error.message}`, 'error');
        }
    }

    handleMessageChunk(messageId, chunk) {
        this.hideTypingIndicator();

        if (this.currentMessageId !== messageId) {
            // New message from assistant
            this.currentMessageId = messageId;
            this.currentMessageContent = '';
            this.createAssistantMessage(messageId);
        }

        // Append chunk
        this.currentMessageContent += chunk;
        this.updateMessageContent(messageId, this.currentMessageContent);
    }

    handleMessageComplete(messageId) {
        this.hideTypingIndicator();

        // Final render with full markdown
        const contentEl = document.querySelector(`[data-message-id="${messageId}"] .message-body`);
        if (contentEl) {
            contentEl.innerHTML = marked.parse(this.currentMessageContent);
            // Re-highlight code blocks
            contentEl.querySelectorAll('pre code').forEach((block) => {
                hljs.highlightElement(block);
            });
        }

        this.currentMessageId = null;
        this.currentMessageContent = '';
        this.scrollToBottom();
    }

    addMessageToUI(role, content) {
        const messageList = document.getElementById('messageList');
        const isUser = role === 'user';

        const messageEl = document.createElement('div');
        messageEl.className = `message ${isUser ? 'message-user' : 'message-assistant'}`;
        messageEl.innerHTML = `
            <div class="message-avatar">${isUser ? 'Y' : this.currentAgent[0]}</div>
            <div class="message-content">
                <div class="message-header">
                    <span class="message-author">${isUser ? 'You' : this.currentAgent}</span>
                    <span class="message-time">${this.formatTime(new Date())}</span>
                </div>
                <div class="message-body">${isUser ? this.escapeHtml(content) : marked.parse(content)}</div>
            </div>
        `;

        messageList.appendChild(messageEl);
        this.scrollToBottom();
    }

    createAssistantMessage(messageId) {
        const messageList = document.getElementById('messageList');

        const messageEl = document.createElement('div');
        messageEl.className = 'message message-assistant';
        messageEl.dataset.messageId = messageId;
        messageEl.innerHTML = `
            <div class="message-avatar">${this.currentAgent[0]}</div>
            <div class="message-content">
                <div class="message-header">
                    <span class="message-author">${this.currentAgent}</span>
                    <span class="message-time">${this.formatTime(new Date())}</span>
                </div>
                <div class="message-body"></div>
            </div>
        `;

        messageList.appendChild(messageEl);
    }

    updateMessageContent(messageId, content) {
        const contentEl = document.querySelector(`[data-message-id="${messageId}"] .message-body`);
        if (contentEl) {
            // Show plain text while streaming for performance
            contentEl.textContent = content;
            this.scrollToBottom();
        }
    }

    addSystemMessage(content, type = 'info') {
        const messageList = document.getElementById('messageList');
        const messageEl = document.createElement('div');
        messageEl.className = `message message-system message-${type}`;
        messageEl.innerHTML = `
            <div class="message-content" style="margin-left: 44px;">
                <div class="message-body">${this.escapeHtml(content)}</div>
            </div>
        `;
        messageList.appendChild(messageEl);
        this.scrollToBottom();
    }

    showTypingIndicator() {
        document.getElementById('typingIndicator').classList.remove('hidden');
        this.scrollToBottom();
    }

    hideTypingIndicator() {
        document.getElementById('typingIndicator').classList.add('hidden');
    }

    showToolNotification(toolName, status) {
        const notification = document.getElementById('toolNotification');
        notification.querySelector('.tool-name').textContent = toolName;
        notification.querySelector('.tool-status').textContent = status;
        notification.classList.remove('hidden');
    }

    hideToolNotification() {
        document.getElementById('toolNotification').classList.add('hidden');
    }

    showConnectionStatus(message) {
        // Could add a status bar in the UI
        console.log('Connection status:', message);
    }

    hideConnectionStatus() {
        console.log('Connection restored');
    }

    async selectAgent(agentName) {
        this.currentAgent = agentName;
        try {
            await this.connection.invoke('SelectAgent', agentName);
        } catch (error) {
            console.error('Select agent error:', error);
        }
        this.updateAgentDisplay({ name: agentName });
    }

    updateAgentDisplay(agentInfo) {
        document.getElementById('agentAvatar').textContent = agentInfo.name ? agentInfo.name[0] : 'A';
        document.getElementById('agentName').textContent = agentInfo.name || 'Unknown';
        document.getElementById('agentDescription').textContent = agentInfo.description || '';
    }

    async createSession() {
        try {
            await this.connection.invoke('CreateSession');
        } catch (error) {
            console.error('Create session error:', error);
        }
    }

    async createNewSession() {
        this.clearMessageList();
        this.showWelcomeMessage();
        await this.createSession();
    }

    async clearSession() {
        if (this.sessionId) {
            try {
                await this.connection.invoke('ClearSession', this.sessionId);
            } catch (error) {
                console.error('Clear session error:', error);
            }
        }
    }

    clearMessageList() {
        const messageList = document.getElementById('messageList');
        messageList.innerHTML = '';
    }

    showWelcomeMessage() {
        const messageList = document.getElementById('messageList');
        messageList.innerHTML = `
            <div class="welcome-message">
                <div class="welcome-icon">
                    <svg width="48" height="48" viewBox="0 0 16 16" fill="currentColor">
                        <path d="M8 15c4.418 0 8-3.134 8-7s-3.582-7-8-7-8 3.134-8 7c0 1.76.743 3.37 1.97 4.6-.097 1.016-.417 2.13-.771 2.966-.079.186.074.394.273.362 2.256-.37 3.597-.938 4.18-1.234A9.06 9.06 0 0 0 8 15z"/>
                    </svg>
                </div>
                <h1>Welcome to AI Chat</h1>
                <p>Select an agent and start a conversation. Your messages will stream in real-time.</p>
                <div class="feature-list">
                    <div class="feature">
                        <span class="feature-icon">S</span>
                        <span>Streaming Responses</span>
                    </div>
                    <div class="feature">
                        <span class="feature-icon">A</span>
                        <span>Multiple Agents</span>
                    </div>
                    <div class="feature">
                        <span class="feature-icon">T</span>
                        <span>Tool Calling</span>
                    </div>
                    <div class="feature">
                        <span class="feature-icon">M</span>
                        <span>Markdown Support</span>
                    </div>
                </div>
            </div>
        `;
    }

    hideWelcomeMessage() {
        const welcome = document.querySelector('.welcome-message');
        if (welcome) {
            welcome.remove();
        }
    }

    async loadAgents() {
        try {
            const agents = await this.connection.invoke('GetAvailableAgents');
            const select = document.getElementById('agentSelect');
            select.innerHTML = agents.map(a =>
                `<option value="${a.name}">${a.name} - ${a.description}</option>`
            ).join('');

            if (agents.length > 0) {
                this.selectAgent(agents[0].name);
            }
        } catch (error) {
            console.error('Load agents error:', error);
        }
    }

    toggleTheme() {
        const container = document.getElementById('chatContainer');
        const currentTheme = container.dataset.theme;
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        container.dataset.theme = newTheme;
        localStorage.setItem('chat-theme', newTheme);
    }

    async handleFileSelect(files) {
        if (!files || files.length === 0) return;

        const filePreview = document.getElementById('filePreview');
        const previewList = filePreview.querySelector('.file-preview-list');

        for (const file of files) {
            const formData = new FormData();
            formData.append('file', file);

            try {
                const response = await fetch('/api/upload', {
                    method: 'POST',
                    body: formData
                });

                if (response.ok) {
                    const attachment = await response.json();
                    this.attachedFiles.push(attachment);

                    const item = document.createElement('div');
                    item.className = 'file-preview-item';
                    item.innerHTML = `
                        <span>${attachment.fileName}</span>
                        <button type="button" onclick="chatApp.removeFile('${attachment.id}')">&times;</button>
                    `;
                    previewList.appendChild(item);
                    filePreview.classList.remove('hidden');
                }
            } catch (error) {
                console.error('File upload error:', error);
            }
        }

        // Clear file input
        document.getElementById('fileInput').value = '';
    }

    removeFile(fileId) {
        this.attachedFiles = this.attachedFiles.filter(f => f.id !== fileId);
        const filePreview = document.getElementById('filePreview');
        const previewList = filePreview.querySelector('.file-preview-list');

        // Re-render preview list
        previewList.innerHTML = this.attachedFiles.map(f => `
            <div class="file-preview-item">
                <span>${f.fileName}</span>
                <button type="button" onclick="chatApp.removeFile('${f.id}')">&times;</button>
            </div>
        `).join('');

        if (this.attachedFiles.length === 0) {
            filePreview.classList.add('hidden');
        }
    }

    scrollToBottom() {
        const messageList = document.getElementById('messageList');
        messageList.scrollTop = messageList.scrollHeight;
    }

    formatTime(date) {
        return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// Initialize application when DOM is ready
let chatApp;
document.addEventListener('DOMContentLoaded', () => {
    chatApp = new ChatApp();
});
