namespace WindowScatter
{
    public class HotkeyParser
    {
        public int ModifierKeys { get; set; }
        public int VirtualKeyCode { get; set; }

        public static HotkeyParser Parse(string hotkey)
        {
            var parts = hotkey.Split('+');
            var result = new HotkeyParser();

            foreach (var part in parts)
            {
                var key = part.Trim().ToLower();

                switch (key)
                {
                    case "ctrl":
                    case "control":
                        result.ModifierKeys |= 0x01;
                        break;

                    case "alt":
                        result.ModifierKeys |= 0x02;
                        break;

                    case "shift":
                        result.ModifierKeys |= 0x04;
                        break;

                    case "win":
                    case "windows":
                        result.ModifierKeys |= 0x08;
                        break;

                    default:
                        result.VirtualKeyCode = GetVirtualKeyCode(key);
                        break;
                }
            }

            return result;
        }

        private static int GetVirtualKeyCode(string keyName)
        {
            keyName = keyName.ToLower();

            if (keyName.Length == 1 && char.IsLetter(keyName[0]))
                return char.ToUpper(keyName[0]);

            if (keyName.Length == 1 && char.IsDigit(keyName[0]))
                return 0x30 + (keyName[0] - '0');

            switch (keyName)
            {
                case "tab": return 0x09;
                case "enter": return 0x0D;
                case "space": return 0x20;
                case "esc":
                case "escape": return 0x1B;
                case "backspace": return 0x08;
                case "delete": return 0x2E;
                case "insert": return 0x2D;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pageup": return 0x21;
                case "pagedown": return 0x22;
                case "left": return 0x25;
                case "up": return 0x26;
                case "right": return 0x27;
                case "down": return 0x28;
                case "f1": return 0x70;
                case "f2": return 0x71;
                case "f3": return 0x72;
                case "f4": return 0x73;
                case "f5": return 0x74;
                case "f6": return 0x75;
                case "f7": return 0x76;
                case "f8": return 0x77;
                case "f9": return 0x78;
                case "f10": return 0x79;
                case "f11": return 0x7A;
                case "f12": return 0x7B;
                default: return 0;
            }
        }
    }
}
