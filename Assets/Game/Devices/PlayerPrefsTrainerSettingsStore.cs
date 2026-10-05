using UnityEngine;
using WindTraceRide.Core;

namespace WindTraceRide.Devices
{
    public sealed class PlayerPrefsTrainerSettingsStore : ITrainerSettingsStore
    {
        private const string IdKey = "trainer.saved.id";
        private const string NameKey = "trainer.saved.name";
        private const string ProtocolKey = "trainer.saved.protocol";

        public SavedTrainer Load()
        {
            var id = PlayerPrefs.GetString(IdKey, string.Empty);
            if (string.IsNullOrWhiteSpace(id)) return null;
            return new SavedTrainer(
                id,
                PlayerPrefs.GetString(NameKey, "Saved trainer"),
                (TrainerProtocol)PlayerPrefs.GetInt(ProtocolKey, (int)TrainerProtocol.Unknown));
        }

        public void Save(SavedTrainer trainer)
        {
            if (trainer == null) return;
            PlayerPrefs.SetString(IdKey, trainer.Id);
            PlayerPrefs.SetString(NameKey, trainer.Name);
            PlayerPrefs.SetInt(ProtocolKey, (int)trainer.Protocol);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(IdKey);
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.DeleteKey(ProtocolKey);
            PlayerPrefs.Save();
        }
    }
}

