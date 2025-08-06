using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.Localization
{
    /// <summary>
    /// Default resource provider implementation
    /// </summary>
    public class DefaultResourceProvider : IResourceProvider
    {
        private readonly Dictionary<string, Dictionary<string, string>> _embeddedResources;

        public DefaultResourceProvider()
        {
            _embeddedResources = new Dictionary<string, Dictionary<string, string>>();
            InitializeEmbeddedResources();
        }

        /// <summary>
        /// Loads resources for a specific culture
        /// </summary>
        public async Task<Dictionary<string, string>> LoadResourcesAsync(string cultureName)
        {
            await Task.Delay(1); // Simulate async operation
            
            if (_embeddedResources.TryGetValue(cultureName, out var resources))
            {
                return new Dictionary<string, string>(resources);
            }
            
            // Try to find a fallback culture
            var languageCode = cultureName.Split('-')[0];
            var fallbackCulture = _embeddedResources.Keys
                .FirstOrDefault(c => c.StartsWith(languageCode));
            
            if (fallbackCulture != null && _embeddedResources.TryGetValue(fallbackCulture, out var fallbackResources))
            {
                return new Dictionary<string, string>(fallbackResources);
            }
            
            return null;
        }

        /// <summary>
        /// Saves resources for a specific culture
        /// </summary>
        public async Task<bool> SaveResourcesAsync(string cultureName, Dictionary<string, string> resources)
        {
            await Task.Delay(1); // Simulate async operation
            
            try
            {
                _embeddedResources[cultureName] = new Dictionary<string, string>(resources);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets available cultures
        /// </summary>
        public async Task<string[]> GetAvailableCulturesAsync()
        {
            await Task.Delay(1); // Simulate async operation
            return _embeddedResources.Keys.ToArray();
        }

        /// <summary>
        /// Initializes embedded resources
        /// </summary>
        private void InitializeEmbeddedResources()
        {
            // English (US)
            _embeddedResources["en-US"] = new Dictionary<string, string>
            {
                ["app.name"] = "LocalTalk",
                ["app.description"] = "Share files across devices",
                ["app.version"] = "Version {0}",
                
                // Transfer strings
                ["transfer.sending"] = "Sending...",
                ["transfer.receiving"] = "Receiving...",
                ["transfer.completed"] = "Transfer completed",
                ["transfer.failed"] = "Transfer failed",
                ["transfer.cancelled"] = "Transfer cancelled",
                ["transfer.paused"] = "Transfer paused",
                ["transfer.resumed"] = "Transfer resumed",
                ["transfer.speed"] = "Speed: {0}",
                ["transfer.eta"] = "ETA: {0}",
                ["transfer.progress"] = "{0}% complete",
                
                // File strings
                ["file.count.singular"] = "{0} file",
                ["file.count.plural"] = "{0} files",
                ["file.size"] = "Size: {0}",
                ["file.selected"] = "Selected: {0}",
                ["file.type.image"] = "Image",
                ["file.type.video"] = "Video",
                ["file.type.audio"] = "Audio",
                ["file.type.document"] = "Document",
                ["file.type.archive"] = "Archive",
                ["file.type.other"] = "Other",
                
                // Device strings
                ["device.discovered"] = "Device discovered",
                ["device.connected"] = "Connected to device",
                ["device.disconnected"] = "Disconnected from device",
                ["device.name"] = "Device: {0}",
                ["device.type.phone"] = "Phone",
                ["device.type.tablet"] = "Tablet",
                ["device.type.computer"] = "Computer",
                ["device.type.unknown"] = "Unknown Device",
                
                // Error strings
                ["error.network"] = "Network error occurred",
                ["error.permission"] = "Permission denied",
                ["error.file.not.found"] = "File not found",
                ["error.disk.space"] = "Insufficient disk space",
                ["error.connection.failed"] = "Connection failed",
                ["error.timeout"] = "Operation timed out",
                ["error.unknown"] = "An unknown error occurred",
                
                // UI strings
                ["ui.ok"] = "OK",
                ["ui.cancel"] = "Cancel",
                ["ui.yes"] = "Yes",
                ["ui.no"] = "No",
                ["ui.retry"] = "Retry",
                ["ui.close"] = "Close",
                ["ui.settings"] = "Settings",
                ["ui.about"] = "About",
                ["ui.help"] = "Help",
                
                // Settings strings
                ["settings.general"] = "General",
                ["settings.network"] = "Network",
                ["settings.security"] = "Security",
                ["settings.language"] = "Language",
                ["settings.theme"] = "Theme",
                ["settings.notifications"] = "Notifications",
                
                // Status strings
                ["status.ready"] = "Ready",
                ["status.connecting"] = "Connecting...",
                ["status.connected"] = "Connected",
                ["status.disconnected"] = "Disconnected",
                ["status.scanning"] = "Scanning for devices...",
                ["status.idle"] = "Idle"
            };

            // Spanish (Spain)
            _embeddedResources["es-ES"] = new Dictionary<string, string>
            {
                ["app.name"] = "LocalTalk",
                ["app.description"] = "Compartir archivos entre dispositivos",
                ["app.version"] = "Versión {0}",
                
                ["transfer.sending"] = "Enviando...",
                ["transfer.receiving"] = "Recibiendo...",
                ["transfer.completed"] = "Transferencia completada",
                ["transfer.failed"] = "Transferencia fallida",
                ["transfer.cancelled"] = "Transferencia cancelada",
                ["transfer.paused"] = "Transferencia pausada",
                ["transfer.resumed"] = "Transferencia reanudada",
                ["transfer.speed"] = "Velocidad: {0}",
                ["transfer.eta"] = "Tiempo estimado: {0}",
                ["transfer.progress"] = "{0}% completado",
                
                ["file.count.singular"] = "{0} archivo",
                ["file.count.plural"] = "{0} archivos",
                ["file.size"] = "Tamaño: {0}",
                ["file.selected"] = "Seleccionado: {0}",
                
                ["device.discovered"] = "Dispositivo descubierto",
                ["device.connected"] = "Conectado al dispositivo",
                ["device.disconnected"] = "Desconectado del dispositivo",
                ["device.name"] = "Dispositivo: {0}",
                
                ["error.network"] = "Error de red",
                ["error.permission"] = "Permiso denegado",
                ["error.file.not.found"] = "Archivo no encontrado",
                ["error.disk.space"] = "Espacio en disco insuficiente",
                ["error.connection.failed"] = "Conexión fallida",
                ["error.timeout"] = "Tiempo de espera agotado",
                ["error.unknown"] = "Error desconocido",
                
                ["ui.ok"] = "Aceptar",
                ["ui.cancel"] = "Cancelar",
                ["ui.yes"] = "Sí",
                ["ui.no"] = "No",
                ["ui.retry"] = "Reintentar",
                ["ui.close"] = "Cerrar",
                ["ui.settings"] = "Configuración",
                ["ui.about"] = "Acerca de",
                ["ui.help"] = "Ayuda",
                
                ["status.ready"] = "Listo",
                ["status.connecting"] = "Conectando...",
                ["status.connected"] = "Conectado",
                ["status.disconnected"] = "Desconectado",
                ["status.scanning"] = "Buscando dispositivos...",
                ["status.idle"] = "Inactivo"
            };

            // French (France)
            _embeddedResources["fr-FR"] = new Dictionary<string, string>
            {
                ["app.name"] = "LocalTalk",
                ["app.description"] = "Partager des fichiers entre appareils",
                ["app.version"] = "Version {0}",
                
                ["transfer.sending"] = "Envoi en cours...",
                ["transfer.receiving"] = "Réception en cours...",
                ["transfer.completed"] = "Transfert terminé",
                ["transfer.failed"] = "Transfert échoué",
                ["transfer.cancelled"] = "Transfert annulé",
                ["transfer.paused"] = "Transfert en pause",
                ["transfer.resumed"] = "Transfert repris",
                ["transfer.speed"] = "Vitesse : {0}",
                ["transfer.eta"] = "Temps restant : {0}",
                ["transfer.progress"] = "{0}% terminé",
                
                ["file.count.singular"] = "{0} fichier",
                ["file.count.plural"] = "{0} fichiers",
                ["file.size"] = "Taille : {0}",
                ["file.selected"] = "Sélectionné : {0}",
                
                ["device.discovered"] = "Appareil découvert",
                ["device.connected"] = "Connecté à l'appareil",
                ["device.disconnected"] = "Déconnecté de l'appareil",
                ["device.name"] = "Appareil : {0}",
                
                ["error.network"] = "Erreur réseau",
                ["error.permission"] = "Permission refusée",
                ["error.file.not.found"] = "Fichier introuvable",
                ["error.disk.space"] = "Espace disque insuffisant",
                ["error.connection.failed"] = "Connexion échouée",
                ["error.timeout"] = "Délai d'attente dépassé",
                ["error.unknown"] = "Erreur inconnue",
                
                ["ui.ok"] = "OK",
                ["ui.cancel"] = "Annuler",
                ["ui.yes"] = "Oui",
                ["ui.no"] = "Non",
                ["ui.retry"] = "Réessayer",
                ["ui.close"] = "Fermer",
                ["ui.settings"] = "Paramètres",
                ["ui.about"] = "À propos",
                ["ui.help"] = "Aide",
                
                ["status.ready"] = "Prêt",
                ["status.connecting"] = "Connexion...",
                ["status.connected"] = "Connecté",
                ["status.disconnected"] = "Déconnecté",
                ["status.scanning"] = "Recherche d'appareils...",
                ["status.idle"] = "Inactif"
            };

            // German (Germany)
            _embeddedResources["de-DE"] = new Dictionary<string, string>
            {
                ["app.name"] = "LocalTalk",
                ["app.description"] = "Dateien zwischen Geräten teilen",
                ["app.version"] = "Version {0}",
                
                ["transfer.sending"] = "Senden...",
                ["transfer.receiving"] = "Empfangen...",
                ["transfer.completed"] = "Übertragung abgeschlossen",
                ["transfer.failed"] = "Übertragung fehlgeschlagen",
                ["transfer.cancelled"] = "Übertragung abgebrochen",
                ["transfer.paused"] = "Übertragung pausiert",
                ["transfer.resumed"] = "Übertragung fortgesetzt",
                ["transfer.speed"] = "Geschwindigkeit: {0}",
                ["transfer.eta"] = "Verbleibende Zeit: {0}",
                ["transfer.progress"] = "{0}% abgeschlossen",
                
                ["file.count.singular"] = "{0} Datei",
                ["file.count.plural"] = "{0} Dateien",
                ["file.size"] = "Größe: {0}",
                ["file.selected"] = "Ausgewählt: {0}",
                
                ["device.discovered"] = "Gerät entdeckt",
                ["device.connected"] = "Mit Gerät verbunden",
                ["device.disconnected"] = "Von Gerät getrennt",
                ["device.name"] = "Gerät: {0}",
                
                ["error.network"] = "Netzwerkfehler",
                ["error.permission"] = "Berechtigung verweigert",
                ["error.file.not.found"] = "Datei nicht gefunden",
                ["error.disk.space"] = "Unzureichender Speicherplatz",
                ["error.connection.failed"] = "Verbindung fehlgeschlagen",
                ["error.timeout"] = "Zeitüberschreitung",
                ["error.unknown"] = "Unbekannter Fehler",
                
                ["ui.ok"] = "OK",
                ["ui.cancel"] = "Abbrechen",
                ["ui.yes"] = "Ja",
                ["ui.no"] = "Nein",
                ["ui.retry"] = "Wiederholen",
                ["ui.close"] = "Schließen",
                ["ui.settings"] = "Einstellungen",
                ["ui.about"] = "Über",
                ["ui.help"] = "Hilfe",
                
                ["status.ready"] = "Bereit",
                ["status.connecting"] = "Verbinden...",
                ["status.connected"] = "Verbunden",
                ["status.disconnected"] = "Getrennt",
                ["status.scanning"] = "Suche nach Geräten...",
                ["status.idle"] = "Untätig"
            };
        }
    }
}
