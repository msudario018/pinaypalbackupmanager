using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.Tests
{
    public class AIAssistantTests
    {
        [Fact]
        public void ZeroLeak_SanitizesTelegramTokens()
        {
            var raw = "My bot token is 1234567890:ABCdefGHIjklMNOpqrsTUVwxyz1234567 and chat is 987654321";
            var sanitized = AIAssistantService.SanitizePrompt(raw);

            Assert.DoesNotContain("1234567890:ABCdefGHIjklMNOpqrsTUVwxyz1234567", sanitized);
            Assert.Contains("[REDACTED_TELEGRAM_TOKEN]", sanitized);
        }

        [Fact]
        public void ZeroLeak_SanitizesUrlCredentialsAndDatabaseStrings()
        {
            var urlRaw = "Connect to ftp://backup_user:SuperSecretPassword123@ftp.pinaypal.com/files";
            var sanitizedUrl = AIAssistantService.SanitizePrompt(urlRaw);

            Assert.DoesNotContain("SuperSecretPassword123", sanitizedUrl);
            Assert.Contains("ftp://backup_user:[REDACTED]@", sanitizedUrl);

            var dbRaw = "Server=localhost;Database=pinaypal;Uid=admin;Pwd=TopSecretDbPassword!;";
            var sanitizedDb = AIAssistantService.SanitizePrompt(dbRaw);

            Assert.DoesNotContain("TopSecretDbPassword!", sanitizedDb);
            Assert.Contains("[REDACTED_CONNECTION_STRING]", sanitizedDb);
        }

        [Fact]
        public void ZeroLeak_SanitizesApiKeysAndBearerTokens()
        {
            var prompt = "Here is my key sk-abcdef1234567890abcdef12345678 and Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
            var sanitized = AIAssistantService.SanitizePrompt(prompt);

            Assert.DoesNotContain("sk-abcdef1234567890abcdef12345678", sanitized);
            Assert.Contains("[REDACTED_KEY]", sanitized);
            Assert.Contains("[REDACTED]", sanitized);
        }

        [Fact]
        public void MemoryStore_LearnsAndRecallsPreferences()
        {
            AIMemoryStore.ClearAll();

            var (ok, msg) = AIMemoryStore.Remember("preference", "backup_time", "User prefers backups to run at 11:30 PM");
            Assert.True(ok);

            var all = AIMemoryStore.GetAll();
            Assert.NotEmpty(all);
            var item = all.FirstOrDefault(i => i.Key == "backup_time");
            Assert.NotNull(item);
            Assert.Equal("User prefers backups to run at 11:30 PM", item.Content);

            var promptBlock = AIMemoryStore.FormatMemoriesForPrompt();
            Assert.Contains("backup_time", promptBlock);
            Assert.Contains("11:30 PM", promptBlock);

            // Test forgetting
            var forgot = AIMemoryStore.Forget("backup_time");
            Assert.True(forgot);
            Assert.DoesNotContain(AIMemoryStore.GetAll(), i => i.Key == "backup_time");
        }

        [Fact]
        public void MemoryStore_BlocksStoringSecrets()
        {
            AIMemoryStore.ClearAll();

            var (ok, msg) = AIMemoryStore.Remember("preference", "secret_pass", "My password is SuperSecretPassword123!");
            // Should fail or redact credentials
            if (ok)
            {
                var stored = AIMemoryStore.GetAll().First(i => i.Key == "secret_pass");
                Assert.DoesNotContain("SuperSecretPassword123!", stored.Content);
            }
            else
            {
                Assert.Contains("Security Guard", msg);
            }
        }

        [Fact]
        public void LLMReply_ExtractsActionTagCorrectly()
        {
            var rawReply = "I noticed your SQL database snapshot is older than 24 hours. [ACTION: run_backup(service=\"sql\")] I recommend approving this snapshot now.";
            var (clean, action) = AIAssistantService.ExtractActionFromReply(rawReply);

            Assert.NotNull(action);
            Assert.Equal("run_backup", action.ActionType);
            Assert.Equal("sql", action.Parameters["service"]);
            Assert.DoesNotContain("[ACTION:", clean);
            Assert.Contains("I noticed your SQL database snapshot", clean);
        }

        [Fact]
        public void AgentProfile_ProducesDistinctTitlesAndInstructions()
        {
            var cfg = new AIAssistantConfig { AgentProfile = "guardian" };
            Assert.Contains("Guardian", cfg.GetProfileTitle());
            Assert.Contains("SRE Guardian", cfg.GetProfileInstructions());

            cfg.AgentProfile = "specialist";
            Assert.Contains("Specialist", cfg.GetProfileTitle());
            Assert.Contains("Backup & Data Integrity Specialist", cfg.GetProfileInstructions());

            cfg.AgentProfile = "speedy";
            Assert.Contains("Speedy", cfg.GetProfileTitle());
            Assert.Contains("Speedy Minimalist Assistant", cfg.GetProfileInstructions());
        }

        [Theory]
        [InlineData("19900515", "1990-05-15")]
        [InlineData("1990/05/15", "1990-05-15")]
        [InlineData("1990.05.15", "1990-05-15")]
        [InlineData("1990-05-15", "1990-05-15")]
        [InlineData("19851231", "1985-12-31")]
        public void BirthdayFormat_FormatsAsYouTypeCorrectly(string input, string expected)
        {
            var formatted = PinayPalBackupManager.UI.SetupWizardWindow.FormatBirthdayAsYouType(input);
            Assert.Equal(expected, formatted);
        }

        [Theory]
        [InlineData("hi")]
        [InlineData("hello")]
        [InlineData("hey there")]
        [InlineData("good morning")]
        public async Task ProcessUserMessage_CasualGreeting_RespondsNaturallyWithoutActions(string greeting)
        {
            var msg = await AIAssistantService.ProcessUserMessageAsync(greeting);
            Assert.Null(msg.ProposedAction);
            Assert.Contains("Hello", msg.Content);
            Assert.DoesNotContain("Good question — here's the full picture", msg.Content);
        }
    }
}
