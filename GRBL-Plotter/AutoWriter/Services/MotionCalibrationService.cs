using System;

namespace GrblPlotter.AutoWriter.Services
{
    public sealed class MotionCalibrationResult
    {
        public double PulsesPerRevolution { get; private set; }
        public double TravelPerRevolutionMm { get; private set; }
        public double StepsPerMm { get; private set; }
        public double MmPerStep { get; private set; }

        internal MotionCalibrationResult(double pulsesPerRevolution, double travelPerRevolutionMm)
        {
            PulsesPerRevolution = pulsesPerRevolution;
            TravelPerRevolutionMm = travelPerRevolutionMm;
            StepsPerMm = pulsesPerRevolution / travelPerRevolutionMm;
            MmPerStep = 1.0 / StepsPerMm;
            MotionCalibrationService.RequireFinitePositive(StepsPerMm, "stepsPerMm");
            MotionCalibrationService.RequireFinitePositive(MmPerStep, "mmPerStep");
        }
    }

    public static class MotionCalibrationService
    {
        public static MotionCalibrationResult CalculateBelt(double stepAngleDegrees, double microsteps, double beltPitchMm, double pulleyTeeth)
        {
            double pulses = PulsesPerRevolution(stepAngleDegrees, microsteps);
            RequireFinitePositive(beltPitchMm, "beltPitchMm");
            RequireFinitePositive(pulleyTeeth, "pulleyTeeth");
            return Result(pulses, beltPitchMm * pulleyTeeth, "belt travel per revolution");
        }

        public static MotionCalibrationResult CalculateScrew(double stepAngleDegrees, double microsteps, double leadMm)
        {
            double pulses = PulsesPerRevolution(stepAngleDegrees, microsteps);
            RequireFinitePositive(leadMm, "leadMm");
            return Result(pulses, leadMm, "screw travel per revolution");
        }

        public static double PulsesPerRevolution(double stepAngleDegrees, double microsteps)
        {
            RequireFinitePositive(stepAngleDegrees, "stepAngleDegrees");
            RequireFinitePositive(microsteps, "microsteps");
            return RequireFinitePositive(360.0 / stepAngleDegrees * microsteps, "pulsesPerRevolution");
        }

        // The drive convention is explicit: internal = external * numerator / denominator.
        // Therefore external = internal * denominator / numerator.
        public static double EffectiveExternalPulsesPerRevolution(double internalPulsesPerRevolution, double ratioNumerator, double ratioDenominator)
        {
            RequireFinitePositive(internalPulsesPerRevolution, "internalPulsesPerRevolution");
            RequireFinitePositive(ratioNumerator, "ratioNumerator");
            RequireFinitePositive(ratioDenominator, "ratioDenominator");
            return RequireFinitePositive(internalPulsesPerRevolution * ratioDenominator / ratioNumerator, "externalPulsesPerRevolution");
        }

        public static MotionCalibrationResult CalculateEffective(double internalPulsesPerRevolution, double leadMm, double ratioNumerator, double ratioDenominator)
        {
            double externalPulses = EffectiveExternalPulsesPerRevolution(internalPulsesPerRevolution, ratioNumerator, ratioDenominator);
            RequireFinitePositive(leadMm, "leadMm");
            return Result(externalPulses, leadMm, "effective travel per revolution");
        }

        public static double CorrectMeasuredStepsPerMm(double oldStepsPerMm, double commandedDistanceMm, double measuredDistanceMm)
        {
            RequireFinitePositive(oldStepsPerMm, "oldStepsPerMm");
            RequireFinitePositive(commandedDistanceMm, "commandedDistanceMm");
            RequireFinitePositive(measuredDistanceMm, "measuredDistanceMm");
            return RequireFinitePositive(oldStepsPerMm * commandedDistanceMm / measuredDistanceMm, "correctedStepsPerMm");
        }

        private static MotionCalibrationResult Result(double pulses, double travel, string name)
        {
            RequireFinitePositive(pulses, "pulsesPerRevolution");
            RequireFinitePositive(travel, name);
            return new MotionCalibrationResult(pulses, travel);
        }

        internal static double RequireFinitePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, value, name + " must be finite and greater than zero.");
            return value;
        }
    }
}
