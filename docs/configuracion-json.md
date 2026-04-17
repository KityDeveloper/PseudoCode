# Configuracion JSON

PseudoCode carga settings desde JSON para que puedas reemplazar el dialecto activo y los colores de sintaxis sin recompilar la app.

## Indice

- [Rutas](#rutas)
- [Settings principales](#settings-principales)
- [Dialectos](#dialectos)
- [Temas de sintaxis](#temas-de-sintaxis)
- [Fallback y validacion](#fallback-y-validacion)
- [Ejemplo: cambiar Escribir por Mostrar](#ejemplo-cambiar-escribir-por-mostrar)

## Rutas

Defaults empaquetados con la app:

```text
settings/default-settings.json
settings/dialects/pseint.json
settings/syntax-themes/dark.json
settings/syntax-themes/light.json
```

Overrides de usuario:

```text
Windows: %APPDATA%/PseudoCode/settings/
Linux: ~/.config/PseudoCode/settings/
macOS: ~/Library/Application Support/PseudoCode/settings/
```

## Settings principales

`default-settings.json` define el dialecto y los temas activos:

```json
{
  "language": {
    "activeDialect": "pseint"
  },
  "editor": {
    "syntaxThemeDark": "dark",
    "syntaxThemeLight": "light"
  }
}
```

El dialecto principal por defecto es `pseint`. El tema principal por defecto es `dark`.

## Dialectos

Un dialecto reemplaza el lenguaje activo. No hay aliases dentro de un dialecto: cada rol tiene una palabra activa.

```json
{
  "id": "pseint",
  "displayName": "PSeInt",
  "keywords": {
    "algorithmStart": "Algoritmo",
    "algorithmEnd": "FinAlgoritmo",
    "processStart": "Proceso",
    "processEnd": "FinProceso",
    "declare": "Definir",
    "typeSeparator": "Como",
    "write": "Escribir",
    "read": "Leer",
    "if": "Si",
    "then": "Entonces",
    "else": "Sino",
    "endIf": "FinSi",
    "while": "Mientras",
    "do": "Hacer",
    "endWhile": "FinMientras",
    "for": "Para",
    "until": "Hasta",
    "step": "Paso",
    "endFor": "FinPara",
    "switch": "Segun",
    "otherwise": "De Otro Modo",
    "endSwitch": "FinSegun",
    "true": "Verdadero",
    "false": "Falso",
    "and": "Y",
    "or": "O",
    "not": "NO"
  },
  "types": ["Entero", "Real", "Cadena", "Caracter", "Logico", "Booleano"],
  "snippets": [
    {
      "text": "Algoritmo",
      "insertText": "Algoritmo MiPrograma\n    \nFinAlgoritmo",
      "description": "Define el inicio y fin de un algoritmo.",
      "isTemplate": true
    }
  ]
}
```

Si cambias `write` de `Escribir` a `Mostrar`, entonces `Escribir` deja de ser valido en ese dialecto.

## Temas de sintaxis

Un tema de sintaxis cambia solo los colores del editor.

```json
{
  "id": "dark",
  "displayName": "Dark",
  "syntax": {
    "keyword": "#5EA1FF",
    "type": "#4EC9B0",
    "string": "#CE9178",
    "number": "#B5CEA8",
    "operator": "#DCDCAA",
    "comment": "#6A9955",
    "blockBackground": "#1F3B4D",
    "diagnosticUnderline": "#FF4D4D"
  }
}
```

## Fallback y validacion

Si un JSON falta, tiene formato invalido o no contiene campos requeridos, PseudoCode usa defaults seguros y muestra un diagnostico. Esto evita que la app quede inutilizable por una configuracion rota.

## Ejemplo: cambiar Escribir por Mostrar

1. Copia `settings/dialects/pseint.json` a la carpeta de usuario como `dialects/custom.json`.
2. Cambia:

```json
"id": "custom",
"displayName": "Custom",
"write": "Mostrar"
```

3. Cambia el settings de usuario:

```json
{
  "language": {
    "activeDialect": "custom"
  },
  "editor": {
    "syntaxThemeDark": "dark",
    "syntaxThemeLight": "light"
  }
}
```

4. Reinicia la app.

Ahora el dialecto acepta `Mostrar "Hola"` y ya no acepta `Escribir "Hola"`.
