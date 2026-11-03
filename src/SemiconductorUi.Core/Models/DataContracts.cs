using System;

namespace SemiconductorUi.Models
{
	[Serializable]
	public class RecipeSnapshot
	{
		public string Name;
		public int WaferCount;
		public int DurA, DurB, DurC;
		public bool SecondExposure;
		public Triple PMA = new Triple();
		public Triple PMB = new Triple();
		public Triple PMC = new Triple();
		public GasRf GasRfPMA = new GasRf();
		public GasRf GasRfPMB = new GasRf();
		public GasRf GasRfPMC = new GasRf();

		[Serializable]
		public class Triple
		{
			public double T;
			public double P;
			public double H;
		}

		[Serializable]
		public class GasRf
		{
			public double NF3;
			public double O2;
			public double CF4;
			public double RF;
		}
	}

	[Serializable]
	public class EnvThresholdSnapshot
	{
		public double TempWarn, TempAlarm;
		public double PressWarnRatio, PressAlarmRatio, PressWarnAbs, PressAlarmAbs;
		public double RfWarnRatio, RfAlarmRatio;
		public double GasWarn, GasAlarm, GasLeakWarn, GasLeakAlarm;
	}
}

