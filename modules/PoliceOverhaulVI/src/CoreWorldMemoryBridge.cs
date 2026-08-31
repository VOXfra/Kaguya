using System;
using System.Reflection;

namespace VOX.PoliceOverhaulVI
{
    internal static class CoreWorldMemoryBridge
    {
        private static MethodInfo _publish;
        private static int _nextResolveAt;

        public static bool Publish(string category, string type, float x, float y, float z, int severity,
            int suspectModelHash, string source, double ttlHours, string tags)
        {
            try
            {
                if (_publish == null && Environment.TickCount >= _nextResolveAt)
                {
                    _nextResolveAt = Environment.TickCount + 5000;
                    Type bridge = Type.GetType("VOX.CoreVI.WorldMemoryBridge, VOXCoreVI", false);
                    if (bridge != null)
                    {
                        _publish = bridge.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(string), typeof(string), typeof(float), typeof(float), typeof(float), typeof(int), typeof(int), typeof(string), typeof(double), typeof(string) },
                            null);
                    }
                }

                if (_publish == null) return false;
                _publish.Invoke(null, new object[] { category, type, x, y, z, severity, suspectModelHash, source, ttlHours, tags });
                return true;
            }
            catch
            {
                _publish = null;
                _nextResolveAt = Environment.TickCount + 10000;
                return false;
            }
        }
    }
}
