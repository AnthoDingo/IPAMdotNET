# IPAMdotNet.ScanAgent

Agent de scan distant d'IPAMdotNet : à installer sur une machine d'un réseau que le serveur ne peut pas joindre.
Il interroge le serveur chaque minute (`/api/agent/work`), sonde les adresses (ping, ports TCP), résout les noms et
renvoie les résultats. Les réglages du scan se font côté serveur (Administration › Agents de scan).

## Configuration

Créer l'agent dans Administration › Agents de scan, puis renseigner `appsettings.json` à côté de l'exécutable :

```json
{
  "Agent": {
    "ServerUrl": "https://ipam.example.com",
    "Key": "<clé affichée à la création>",
    "PollSeconds": 60,
    "AutoUpdate": true
  }
}
```

Les mêmes valeurs peuvent venir de variables d'environnement : `Agent__ServerUrl`, `Agent__Key`…

## Service Windows

Dans une invite de commandes administrateur :

```
sc.exe create IPAMdotNet.ScanAgent binPath= "C:\IPAMdotNet.ScanAgent\IPAMdotNet.ScanAgent.exe" start= delayed-auto
sc.exe failure IPAMdotNet.ScanAgent reset= 86400 actions= restart/5000/restart/5000/restart/60000
sc.exe start IPAMdotNet.ScanAgent
```

Le compte « Système local » (par défaut) peut envoyer des pings. Journal : Observateur d'événements › Application.

## Service systemd (Linux)

`/etc/systemd/system/ipamdotnet-scanagent.service` :

```ini
[Unit]
Description=IPAMdotNet scan agent
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=/opt/ipamdotnet-scanagent/IPAMdotNet.ScanAgent
WorkingDirectory=/opt/ipamdotnet-scanagent
Restart=always
RestartSec=5
User=ipamagent
# Ping ICMP sans root
AmbientCapabilities=CAP_NET_RAW

[Install]
WantedBy=multi-user.target
```

```
sudo systemctl daemon-reload && sudo systemctl enable --now ipamdotnet-scanagent
journalctl -u ipamdotnet-scanagent -f
```

## Mise à jour automatique

Le serveur publié embarque les paquets de l'agent (`agent/IPAMdotNet.ScanAgent-<plateforme>.zip`) et annonce sa version.
Un agent plus ancien télécharge son paquet, remplace ses fichiers (sauf `appsettings*.json`) et s'arrête avec le code 3 :
le gestionnaire de services le relance (`Restart=always` sous systemd, actions de récupération ci-dessus sous Windows).
L'utilisateur du service doit pouvoir écrire dans le dossier de l'agent. `"AutoUpdate": false` désactive ce comportement.
