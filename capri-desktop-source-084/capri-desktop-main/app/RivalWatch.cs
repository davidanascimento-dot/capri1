using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CapriDesktop
{
    internal static class RivalWatch
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);

        // The foreground title is inspected only in memory to identify the AI site; it is never stored or sent.
        internal static string Detect(int ownProcess)
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            int pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid <= 0 || pid == ownProcess) return null;
            string process = "";
            try { using (Process found = Process.GetProcessById(pid)) process = found.ProcessName.ToLowerInvariant(); }
            catch (Exception) { }
            string title = "";
            try { StringBuilder text = new StringBuilder(512); GetWindowText(window, text, text.Capacity); title = text.ToString().ToLowerInvariant(); }
            catch (Exception) { }

            if (process.Contains("claude") || title.Contains("claude")) return "Claude";
            if (process.Contains("codex") || title.Contains("codex")) return "Codex";
            if (process.Contains("chatgpt") || title.Contains("chatgpt") || title.Contains("chat.openai")) return "ChatGPT";
            if (process.Contains("gemini") || title.Contains("gemini") || title.Contains("google ai studio")) return "Gemini";
            if (process.Contains("antigravity") || title.Contains("antigravity")) return "Antigravity";
            if (process.Contains("copilot") || title.Contains("copilot")) return "Copilot";
            if (process.Contains("perplexity") || title.Contains("perplexity")) return "Perplexity";
            if (process.Contains("deepseek") || title.Contains("deepseek")) return "DeepSeek";
            if (process.Contains("grok") || title.Contains("grok")) return "Grok";
            if (title.Contains("le chat") || title.Contains("mistral.ai")) return "Le Chat";
            if (title.Contains("character.ai") || title.Contains("character ai")) return "Character.AI";
            if (title.Contains("meta.ai") || title.Contains("meta ai")) return "Meta AI";
            if (title.Contains("poe.com") || title.StartsWith("poe -")) return "Poe";
            if (title.Contains("qwen chat") || title.Contains("chat.qwen")) return "Qwen";
            if (title.Contains("kimi.com") || title.Contains("kimi -")) return "Kimi";
            return null;
        }
    }
}
