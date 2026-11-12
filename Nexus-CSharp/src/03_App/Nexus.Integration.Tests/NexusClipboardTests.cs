using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Nexus.App.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests
{
    public class NexusClipboardTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        public NexusClipboardTests(ITestOutputHelper output)
        {
            _output = output;
            NexusClipboard.CustomSetTextAction = null;
            NexusClipboard.CustomNativeSetTextAction = null;
            NexusClipboard.CustomDelayAction = null;
        }

        public void Dispose()
        {
            NexusClipboard.CustomSetTextAction = null;
            NexusClipboard.CustomNativeSetTextAction = null;
            NexusClipboard.CustomDelayAction = null;
        }

        [Fact]
        public void SetText_NormalCopy_Succeeds()
        {
            string? capturedText = null;
            NexusClipboard.CustomSetTextAction = text => capturedText = text;

            bool result = NexusClipboard.SetText("Test clipboard content");

            Assert.True(result);
            Assert.Equal("Test clipboard content", capturedText);
        }

        [Fact]
        public void SetText_TransientComException_RetriesAndSucceeds()
        {
            int attempts = 0;
            var delays = new List<int>();
            string? capturedText = null;

            NexusClipboard.CustomSetTextAction = text =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new COMException("Cannot open clipboard", NexusClipboard.CannotOpenClipboard);
                }
                capturedText = text;
            };

            NexusClipboard.CustomDelayAction = delay => delays.Add(delay);

            bool result = NexusClipboard.SetText("Transient success", maxAttempts: 5, retryDelayMs: 40);

            Assert.True(result);
            Assert.Equal(3, attempts);
            Assert.Equal(2, delays.Count);
            Assert.All(delays, d => Assert.Equal(40, d));
            Assert.Equal("Transient success", capturedText);
        }

        [Fact]
        public void SetText_PersistentComException_FailsGracefully()
        {
            int attempts = 0;
            var delays = new List<int>();

            NexusClipboard.CustomSetTextAction = _ =>
            {
                attempts++;
                throw new COMException("Cannot open clipboard", NexusClipboard.CannotOpenClipboard);
            };

            NexusClipboard.CustomDelayAction = delay => delays.Add(delay);

            bool result = NexusClipboard.SetText("Persistent failure", maxAttempts: 4, retryDelayMs: 25);

            Assert.False(result);
            Assert.Equal(4, attempts);
            Assert.Equal(3, delays.Count);
            Assert.All(delays, d => Assert.Equal(25, d));
        }

        [Theory]
        [InlineData("Single line text")]
        [InlineData("Multi-line\r\ntext\nwith symbols: !@#$%^&*()_+{}[]:;\"'<>,.?/~`")]
        [InlineData("```csharp\r\npublic void Test() => Console.WriteLine(\"Hello\");\r\n```")]
        [InlineData("Unicode: 日本語 🚀 ⚡ 12345")]
        public void SetText_ClipboardContentUnchanged(string originalText)
        {
            string? capturedText = null;
            NexusClipboard.CustomSetTextAction = text => capturedText = text;

            bool result = NexusClipboard.SetText(originalText);

            Assert.True(result);
            Assert.Equal(originalText, capturedText);
        }

        [Fact]
        public void SetText_NullText_ReturnsFalse()
        {
            bool actionCalled = false;
            NexusClipboard.CustomSetTextAction = _ => actionCalled = true;

            bool result = NexusClipboard.SetText(null);

            Assert.False(result);
            Assert.False(actionCalled);
        }

        [Fact]
        public void SetText_NonComException_FailsGracefullyWithoutRetry()
        {
            int attempts = 0;
            NexusClipboard.CustomSetTextAction = _ =>
            {
                attempts++;
                throw new InvalidOperationException("Generic failure");
            };

            bool result = NexusClipboard.SetText("Test non-COM failure", maxAttempts: 5);

            Assert.False(result);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public void SetPlainTextNative_NormalCopy_Succeeds()
        {
            string? capturedText = null;
            NexusClipboard.CustomNativeSetTextAction = text => capturedText = text;

            bool result = NexusClipboard.SetPlainTextNative("Test native clipboard content");

            Assert.True(result);
            Assert.Equal("Test native clipboard content", capturedText);
        }

        [Fact]
        public void SetPlainTextNative_NullText_ReturnsFalse()
        {
            bool actionCalled = false;
            NexusClipboard.CustomNativeSetTextAction = _ => actionCalled = true;

            bool result = NexusClipboard.SetPlainTextNative(null);

            Assert.False(result);
            Assert.False(actionCalled);
        }

        [Theory]
        [InlineData("Single line text")]
        [InlineData("Multi-line\r\ntext\nwith symbols: !@#$%^&*()_+{}[]:;\"'<>,.?/~`")]
        [InlineData("Simbol & tanda kutip \" ' < > % persen")]
        [InlineData("Unicode: 日本語 🚀 ⚡ 12345 äöü café")]
        public void SetPlainTextNative_CustomHook_ClipboardContentUnchanged(string originalText)
        {
            string? capturedText = null;
            NexusClipboard.CustomNativeSetTextAction = text => capturedText = text;

            bool result = NexusClipboard.SetPlainTextNative(originalText);

            Assert.True(result);
            Assert.Equal(originalText, capturedText);
        }

        [Theory]
        [InlineData("Single line native text")]
        [InlineData("Line 1: Special & Symbols <tag> \"quote\" 'single' %percent%\r\nLine 2: Unicode 日本語 🚀 ⚡ äöü café\r\nLine 3: End")]
        [InlineData("Multi-line\r\nwith CRLF and LF\nand symbols & \" < > %")]
        public void SetPlainTextNative_LiveWin32Clipboard_PreservesContentAndProvidesCfUnicodeText(string inputText)
        {
            // Ensure no mock interceptor is active
            NexusClipboard.CustomNativeSetTextAction = null;

            var observations = new List<object>();
            observations.Add(ObserveClipboard("before-write"));
            try
            {
                bool success = NexusClipboard.SetPlainTextNative(inputText);
                observations.Add(new { stage = "native-write-result", success });
                observations.Add(ObserveClipboard("after-write"));
                Assert.True(success);

                var (clipboardText, formatIds, registeredNames) = ReadClipboardNative(observations);

                // 1. Text and line breaks remain intact
                Assert.Equal(inputText, clipboardText);

                // 2. CF_UNICODETEXT (13) is provided
                Assert.Contains(13u, formatIds);

                // 3. Registered OLE private data formats are NOT present on the clipboard
                // Format identity inspected by name, allowing Windows synthesized native formats (CF_TEXT, CF_OEMTEXT, CF_LOCALE)
                Assert.DoesNotContain("DataObject", registeredNames, StringComparer.OrdinalIgnoreCase);
                Assert.DoesNotContain("Ole Private Data", registeredNames, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _output.WriteLine("GATE_H_CLIPBOARD_FAILURE " + JsonSerializer.Serialize(observations));
                throw;
            }
        }

        [Fact]
        public void SetText_OriginalMethod_PreservesBehavior()
        {
            string? capturedText = null;
            NexusClipboard.CustomSetTextAction = text => capturedText = text;

            bool result = NexusClipboard.SetText("Preserved original SetText behavior");

            Assert.True(result);
            Assert.Equal("Preserved original SetText behavior", capturedText);
        }

        [Fact]
        public void NativeRead_OpenScope_CannotBeClosedByAnotherNullWindowOpener()
        {
            const string text = "Native read ownership: 日本語\r\nsecond line";
            Assert.True(NexusClipboard.SetPlainTextNative(text));
            bool competingOpen = false;
            bool competingClose = false;
            var (actual, formats, _) = ReadClipboardNative(afterOpen: () =>
            {
                var competitor = new System.Threading.Thread(() =>
                {
                    competingOpen = OpenClipboard(IntPtr.Zero);
                    if (competingOpen)
                    {
                        competingClose = CloseClipboard();
                    }
                });
                competitor.Start();
                Assert.True(competitor.Join(TimeSpan.FromSeconds(5)));
            });

            Assert.False(competingOpen);
            Assert.False(competingClose);
            Assert.Equal(text, actual);
            Assert.Contains(13u, formats);
        }

        #region Win32 Clipboard Inspection Helpers for Testing

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint extendedStyle, string className,
            string windowName, uint style, int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr window);

        private sealed class ClipboardReaderWindow : IDisposable
        {
            // A distinct message-only HWND makes this read-open scope exclusive.
            // Two NULL-HWND openers can share the open state; either can close it.
            internal IntPtr Handle { get; } = CreateWindowExW(0, "STATIC",
                "Nexus clipboard test reader", 0, 0, 0, 0, 0,
                new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            internal ClipboardReaderWindow()
            {
                if (Handle == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            public void Dispose()
            {
                if (!DestroyWindow(Handle))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static object ObserveClipboard(string stage)
        {
            var sequence = GetClipboardSequenceNumber();
            var owner = GetClipboardOwner();
            var threadId = GetWindowThreadProcessId(owner, out var processId);
            return new { stage, sequence, ownerHwnd = owner.ToInt64(), ownerPid = processId,
                ownerThreadId = threadId, unicodeAvailable = IsClipboardFormatAvailable(13),
                apartment = System.Threading.Thread.CurrentThread.GetApartmentState().ToString() };
        }

        private static (string? text, List<uint> formatIds, List<string> registeredNames) ReadClipboardNative(List<object>? observations = null, Action? afterOpen = null)
        {
            using var readerWindow = new ClipboardReaderWindow();
            observations?.Add(ObserveClipboard("before-read"));
            for (int attempt = 0; attempt < 10; attempt++)
            {
                bool opened = OpenClipboard(readerWindow.Handle);
                int openError = opened ? 0 : Marshal.GetLastWin32Error();
                observations?.Add(new { stage = "open-read", attempt, opened, readerHwnd = readerWindow.Handle.ToInt64(), win32Error = openError });
                if (opened)
                {
                    try
                    {
                        afterOpen?.Invoke();
                        string? text = null;
                        var formatIds = new List<uint>();
                        var registeredNames = new List<string>();

                        uint fmt = 0;
                        while ((fmt = EnumClipboardFormats(fmt)) != 0)
                        {
                            formatIds.Add(fmt);
                            var sb = new StringBuilder(256);
                            if (GetClipboardFormatName(fmt, sb, sb.Capacity) > 0)
                            {
                                registeredNames.Add(sb.ToString());
                            }
                        }

                        IntPtr hData = GetClipboardData(13); // CF_UNICODETEXT
                        int dataError = hData == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                        observations?.Add(new { stage = "get-data", dataHandle = hData.ToInt64(), win32Error = dataError });
                        if (hData != IntPtr.Zero)
                        {
                            IntPtr pLocked = GlobalLock(hData);
                            int lockError = pLocked == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                            observations?.Add(new { stage = "global-lock", pointer = pLocked.ToInt64(), win32Error = lockError });
                            if (pLocked != IntPtr.Zero)
                            {
                                try
                                {
                                    text = Marshal.PtrToStringUni(pLocked);
                                }
                                finally
                                {
                                    GlobalUnlock(hData);
                                }
                            }
                        }

                        observations?.Add(ObserveClipboard("read-locked"));
                        return (text, formatIds, registeredNames);
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                System.Threading.Thread.Sleep(20);
            }
            throw new InvalidOperationException("Failed to open clipboard for reading in test.");
        }

        #endregion
    }
}
