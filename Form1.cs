using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace MouseKeyboardBot
{
    public partial class Form1 : Form
    {
        private bool isRecording = false;
        private bool isPlaying = false;
        private List<InputEvent> recordedEvents = new List<InputEvent>();
        private CancellationTokenSource recordingCancellation;
        private CancellationTokenSource playbackCancellation;
        private bool repeatPlayback = true; // Флаг для определения, нужно ли повторно воспроизводить запись

        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int KEYEVENTF_KEYUP = 0x0002;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_F11 = 0x7A;
        private const int VK_F12 = 0x7B;

        private IntPtr hookId = IntPtr.Zero;
        private LowLevelKeyboardProc keyboardProc;

        public Form1()
        {
            InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            keyboardProc = HookCallback;
            hookId = SetHook(keyboardProc);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(hookId);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_F11)
                {
                    ToggleRecording();
                    return (IntPtr)1;
                }
                else if (vkCode == VK_F12)
                {
                    TogglePlayback();
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private void RecordButton_Click(object sender, EventArgs e)
        {
            ToggleRecording();
        }

        private void PlaybackButton_Click(object sender, EventArgs e)
        {
            TogglePlayback();
        }

        private void ToggleRecording()
        {
            if (!isRecording)
            {
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        private void TogglePlayback()
        {
            if (!isPlaying)
            {
                StartPlayback();
            }
            else
            {
                StopPlayback();
            }
        }

        private void StartRecording()
        {
            isRecording = true;
            StatusText.Text = "Начинаю запись";
            recordedEvents.Clear();
            recordingCancellation = new CancellationTokenSource();
            Task.Run(() => CaptureEvents());
        }

        private void StopRecording()
        {
            isRecording = false;
            StatusText.Text = "Запись готова";
            recordingCancellation?.Cancel();
        }

        private async Task CaptureEvents()
        {
            DateTime lastRecordedEventTime = DateTime.Now;
            bool wasMouseDown = false;
            var keyStates = new Dictionary<Keys, bool>();

            while (!recordingCancellation.Token.IsCancellationRequested)
            {
                Point currentMousePosition = Cursor.Position;
                bool isMouseDown = (GetAsyncKeyState((int)Keys.LButton) & 0x8000) != 0;

                if (currentMousePosition != Point.Empty || isMouseDown || wasMouseDown)
                {
                    InputEvent mouseMoveEvent = new InputEvent
                    {
                        Timestamp = DateTime.Now,
                        Type = InputEventType.MouseMove,
                        Location = currentMousePosition
                    };
                    recordedEvents.Add(mouseMoveEvent);

                    if (isMouseDown && !wasMouseDown)
                    {
                        InputEvent mouseDownEvent = new InputEvent
                        {
                            Timestamp = DateTime.Now,
                            Type = InputEventType.MouseDown,
                            Location = currentMousePosition
                        };
                        recordedEvents.Add(mouseDownEvent);
                    }
                    else if (!isMouseDown && wasMouseDown)
                    {
                        InputEvent mouseUpEvent = new InputEvent
                        {
                            Timestamp = DateTime.Now,
                            Type = InputEventType.MouseUp,
                            Location = currentMousePosition
                        };
                        recordedEvents.Add(mouseUpEvent);
                    }

                    wasMouseDown = isMouseDown;
                }

                foreach (Keys key in Enum.GetValues(typeof(Keys)))
                {
                    bool isKeyDown = (GetAsyncKeyState((int)key) & 0x8000) != 0;
                    bool wasKeyDown = keyStates.TryGetValue(key, out bool value) ? value : false;

                    if (isKeyDown && !wasKeyDown)
                    {
                        InputEvent keyDownEvent = new InputEvent
                        {
                            Timestamp = DateTime.Now,
                            Type = InputEventType.KeyDown,
                            KeyOrButton = (int)key
                        };
                        recordedEvents.Add(keyDownEvent);
                    }
                    else if (!isKeyDown && wasKeyDown)
                    {
                        InputEvent keyUpEvent = new InputEvent
                        {
                            Timestamp = DateTime.Now,
                            Type = InputEventType.KeyUp,
                            KeyOrButton = (int)key
                        };
                        recordedEvents.Add(keyUpEvent);
                    }

                    keyStates[key] = isKeyDown;
                }

                TimeSpan timeSinceLastEvent = DateTime.Now - lastRecordedEventTime;
                lastRecordedEventTime = DateTime.Now;

                // Пауза, чтобы установить правильную задержку между событиями
                int remainingTime = (int)Math.Max(0, 10 - timeSinceLastEvent.TotalMilliseconds);
                await Task.Delay(remainingTime);
            }

            isRecording = false;
            StatusText.Text = "Запись завершена";
        }

        private void StartPlayback()
        {
            repeatPlayback = true; // Устанавливаем флаг зацикливания воспроизведения
            isPlaying = true;
            StatusText.Text = "Воспроизвожу запись";
            Task.Run(async () =>
            {
                do
                {
                    await PlayEvents();
                } while (repeatPlayback);
            });
        }

        private void StopPlayback()
        {
            isPlaying = false;
            StatusText.Text = "Готово";
            repeatPlayback = false;
            playbackCancellation?.Cancel();
        }

        private async Task PlayEvents()
        {
            playbackCancellation = new CancellationTokenSource();
            DateTime previousTimestamp = recordedEvents.FirstOrDefault()?.Timestamp ?? DateTime.Now;

            foreach (InputEvent evt in recordedEvents)
            {
                if (playbackCancellation.Token.IsCancellationRequested)
                    break;

                if (evt.Type == InputEventType.MouseMove)
                {
                    TimeSpan timeSincePreviousEvent = evt.Timestamp - previousTimestamp;
                    await Task.Delay(timeSincePreviousEvent);
                    Cursor.Position = evt.Location;
                    previousTimestamp = evt.Timestamp;
                }
                else if (evt.Type == InputEventType.MouseDown)
                {
                    mouse_event(MOUSEEVENTF_LEFTDOWN, evt.Location.X, evt.Location.Y, 0, UIntPtr.Zero);
                }
                else if (evt.Type == InputEventType.MouseUp)
                {
                    mouse_event(MOUSEEVENTF_LEFTUP, evt.Location.X, evt.Location.Y, 0, UIntPtr.Zero);
                }
                else if (evt.Type == InputEventType.KeyDown)
                {
                    keybd_event((byte)evt.KeyOrButton, 0, 0, UIntPtr.Zero);
                }
                else if (evt.Type == InputEventType.KeyUp)
                {
                    keybd_event((byte)evt.KeyOrButton, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }

            if (repeatPlayback && !playbackCancellation.Token.IsCancellationRequested)
            {
                await Task.Delay(1000); // Задержка перед повторным воспроизведением
                await PlayEvents();
            }
            else
            {
                this.Invoke(new Action(() =>
                {
                    isPlaying = false;
                    StatusText.Text = "Готово";
                }));
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWMOUSE mouse;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RAWMOUSE
        {
            [FieldOffset(0)]
            public ushort usFlags;
            [FieldOffset(4)]
            public uint ulButtons;
            [FieldOffset(4)]
            public ushort usButtonFlags;
            [FieldOffset(6)]
            public ushort usButtonData;
            [FieldOffset(8)]
            public uint ulRawButtons;
            [FieldOffset(12)]
            public int lLastX;
            [FieldOffset(16)]
            public int lLastY;
            [FieldOffset(20)]
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        public class InputEvent
        {
            public DateTime Timestamp { get; set; }
            public InputEventType Type { get; set; }
            public int KeyOrButton { get; set; }
            public Point Location { get; set; }
        }

        public enum InputEventType
        {
            MouseMove,
            MouseDown,
            MouseUp,
            KeyDown,
            KeyUp
        }
    }
}
