using System;
using System.Collections.Generic;

namespace UnityLauncher.Core
{
    public class UnityVersion
    {
        private static Dictionary<char, int> phase_order = new Dictionary<char, int>()
            {{'a', 0}, {'b', 1}, {'f', 2}, {'p', 3}};

        private string version = "";

        private int major = 0;
        private int minor = 0;
        private int patch = 0;
        private int prerelease = 0;
        private char phase = '0';

        public void SetVersion(string version)
        {
            var s = version.Split('.');
            if (s.Length != 3)
                throw new Exception($"The version {version} is invalid. It has to be in the format 2000.1.0f1");
            if (s[0].Length != 4 && s[0].Length != 1)
                throw new Exception($"Failed to parse major version from version {version}");
            major = int.Parse(s[0]);
            minor = int.Parse(s[1]);

            var num = 0;
            while (num < s[2].Length && !Char.IsLetter(s[2][num]))
            {
                num += 1;
            }

            patch = int.Parse(s[2].Substring(0, num));
            phase = s[2][num];
            prerelease = int.Parse(s[2].Substring(num + 1));
            this.version = version;
        }

        public string GetVersion()
        {
            return version;
        }

        public string GetMajorVersion()
        {
            return $"{major}.{minor}";
        }

        public static bool operator <(UnityVersion self, UnityVersion other)
        {
            if (self.major != other.major)
                return self.major < other.major;
            if (self.minor != other.minor)
                return self.minor < other.minor;
            if (self.patch != other.patch)
                return self.patch < other.patch;
            if (self.phase != other.phase)
                return phase_order[self.phase] < phase_order[other.phase];
            return self.prerelease < other.prerelease;
        }

        public static bool operator ==(UnityVersion self, UnityVersion other)
        {
            return self.version == other.version;
        }

        public static bool operator !=(UnityVersion self, UnityVersion other)
        {
            return !(self == other);
        }

        public static bool operator >(UnityVersion v1, UnityVersion v2)
        {
            if (v1 == v2)
                return false;
            if (v1 < v2)
            {
                return false;
            }

            return true;
        }
        
        public static bool operator >=(UnityVersion v1, UnityVersion v2)
        {
            if (v1 == v2)
                return true;
            if (v1 < v2)
            {
                return false;
            }

            return true;
        }

        public static bool operator <=(UnityVersion v1, UnityVersion v2)
        {
            if (v1 == v2)
                return true;
            if (v1 < v2)
            {
                return true;
            }

            return false;
        }
    }
}