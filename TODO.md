# TODO

- [ ] Afficher la licence **avant** l'initialisation de la base dans l'assistant `/setup`.
  Aujourd'hui `AnthoDingo.Setup` n'insère les étapes supplémentaires (`ISetupExtraStep`) qu'après la création du compte admin : un refus de licence laisse une base migrée avec un admin, mais une installation non terminée.
  Pistes : faire évoluer `AnthoDingo.Setup` pour permettre une étape avant l'étape 1, ou remplacer la page intégrée par une page maison (`UseSetupGate()`).
