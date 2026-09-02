using System;

namespace VOX.CharacterRuntimeVI
{
    internal static class FitnessRuntimeBridge
    {
        public static Action<float, float, float> AddTraining;
        public static float CurrentStrength;
        public static float CurrentEndurance;
        public static float CurrentLeanMass;
        public static bool CurrentProfileValid;

        public static void Publish(float strength, float endurance, float leanMass)
        {
            CurrentStrength = strength;
            CurrentEndurance = endurance;
            CurrentLeanMass = leanMass;
            CurrentProfileValid = true;
        }

        public static void ClearPublished()
        {
            CurrentStrength = 0f;
            CurrentEndurance = 0f;
            CurrentLeanMass = 0f;
            CurrentProfileValid = false;
        }

        public static void Train(float strength, float endurance, float leanMass)
        {
            Action<float, float, float> sink = AddTraining;
            if (sink != null) sink(strength, endurance, leanMass);
        }
    }
}
