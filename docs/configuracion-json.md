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
    "fontSize": 15,
    "interfaceScale": 1,
    "syntaxThemeDark": "dark",
    "syntaxThemeLight": "light"
  }
}
```

El dialecto principal por defecto es `pseint`. El tema principal por defecto es `dark`. `fontSize` guarda el tamano del texto del editor y `interfaceScale` guarda el zoom general de la interfaz; ambos se mantienen entre reinicios.

En la app puedes abrir el menu **Configuracion** para editar estos archivos sin salir de PseudoCode:

- **Configuracion > Settings JSON**: edita `default-settings.json`.
- **Configuracion > Temas para el codigo fuente**: edita temas de sintaxis.
- **Configuracion > Configurar sintaxis**: edita dialectos.
- **Configuracion > Carpeta de configuracion**: muestra la carpeta de usuario y crea plantillas.

La experiencia es parecida a editar settings en VS Code: la app guarda una copia JSON en tu perfil de usuario y aplica los cambios con **Guardar y aplicar**, sin reiniciar la app.

Cuando guardas un dialecto desde **Configurar sintaxis**, la app lee el campo `id`, actualiza `default-settings.json` y vuelve a cargar:

- Palabras reservadas del editor.
- Validacion y subrayado de errores.
- Autocompletado y plantillas rapidas.
- Listado y ejemplos del panel de ayuda.

Cuando guardas un tema desde **Temas para el codigo fuente**, la app lee el campo `id`, actualiza el tema del modo activo y repinta el editor al momento. Si estas en modo oscuro cambia `syntaxThemeDark`; si estas en modo claro cambia `syntaxThemeLight`.

La ventana de temas incluye dos formas de editar:

- Editor JSON con resaltado de sintaxis.
- Panel visual de colores con campos hex, vista previa y selector de color.

El panel visual actualiza el JSON automaticamente, asi que puedes ajustar colores sin escribir cada propiedad a mano.

La ventana de settings y dialectos tambien incluye un panel visual por secciones con campos de texto. Al editar un campo, el JSON se actualiza automaticamente; si necesitas algo avanzado, puedes seguir editando el JSON directo.

Tambien puedes abrir **Configuracion > Carpeta de configuracion** para:

- Ver el dialecto activo.
- Ver el tema oscuro y claro activos.
- Copiar la ruta de settings de usuario.
- Crear plantillas `default-settings.json`, `dialects/custom.json` y `syntax-themes/custom-dark.json`.
- Abrir esta guia de configuracion.

## JSON alternativos incluidos

PseudoCode incluye varios ejemplos listos para copiar o seleccionar:

Dialectos:

- `pseint`: dialecto principal estilo PSeInt.
- `pseint-compatible`: variante compatible con palabras base de PSeInt para intercambiar algoritmos.
- `simple`: dialecto de ejemplo para ver como se reemplazan palabras del lenguaje; no busca compatibilidad con PSeInt.
- `english`: dialecto de ejemplo basado en PSeInt pero traducido al ingles, con `Algorithm`, `Write`, `Read`, `If`, `While`, `For` y `Switch`.

Temas:

- `dark`: tema principal.
- `light`: tema para modo claro.
- `high-contrast`: colores de alto contraste.
- `sunset`: paleta alternativa para experimentar.

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
    "clear": "Borrar",
    "screen": "Pantalla",
    "wait": "Esperar",
    "seconds": "Segundos",
    "milliseconds": "Milisegundos",
    "withoutNewline": "Sin Saltar",
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
    "diagnosticUnderline": "#FF4D4D",
    "editorBackground": "#1E1E1E"
  }
}
```

`editorBackground` cambia el fondo del area de codigo. Los demas colores del layout general de la app siguen dependiendo del modo claro/oscuro de la interfaz.

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

3. Guarda desde **Configuracion > Configurar sintaxis** con **Guardar y aplicar**.

La app actualiza automaticamente el settings de usuario para seleccionar el dialecto:

```json
{
  "language": {
    "activeDialect": "custom"
  },
  "editor": {
    "fontSize": 15,
    "interfaceScale": 1,
    "syntaxThemeDark": "dark",
    "syntaxThemeLight": "light"
  }
}
```

Ahora el dialecto acepta `Mostrar "Hola"` y ya no acepta `Escribir "Hola"`.
