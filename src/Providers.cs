namespace UsageLLMs
{
    internal enum Provider { None = -1, Codex, Claude, Antigravity, Grok }

    internal static class Providers
    {
        public static readonly Provider[] All = { Provider.Codex, Provider.Claude, Provider.Antigravity, Provider.Grok };
        private static readonly string[] Names = { "Codex", "Claude Code", "Antigravity", "Grok Build" };
        public const int AllEnabled = (1 << 4) - 1;

        public static string Name(Provider provider)
        {
            return provider == Provider.None ? "Contas" : Names[(int)provider];
        }
    }
}
