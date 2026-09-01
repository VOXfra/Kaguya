using System;

namespace VOX.CharacterRuntimeVI
{
    internal static class FitnessRuntimeBridge
    {
        public static Action<float, float, float> AddTraining;

        public static void Train(float strength, float endurance, float leanMass)
        {
            Action<float, float, float> sink = AddTraining;
            if (sink != null) sink(strength, endurance, leanMass);
        }
    }
}
