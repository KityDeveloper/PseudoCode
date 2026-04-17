# Pseudolenguaje

Esta guia resume la sintaxis principal que PseudoCode entiende hoy. El dialecto por defecto es estilo PSeInt y puede reemplazarse desde JSON, pero los ejemplos usan las palabras originales: `Algoritmo`, `Definir`, `Escribir`, `Leer`, `Si`, `Mientras`, `Para` y `Segun`.

## Estructura base

Todo programa empieza con `Algoritmo` y termina con `FinAlgoritmo`.

```text
Algoritmo Saludo
    Escribir "Hola desde PseudoCode"
FinAlgoritmo
```

Tambien se puede usar `Proceso` y `FinProceso`.

```text
Proceso Saludo
    Escribir "Hola"
FinProceso
```

## Comentarios

Los comentarios usan `//`. Todo lo que queda despues se ignora al ejecutar.

```text
Algoritmo Comentarios
    // Esta linea no se ejecuta
    Escribir "Solo se muestra este texto"
FinAlgoritmo
```

## Tipos y variables

La forma general es `Definir nombre Como Tipo`.

Tipos comunes:

- `Entero`
- `Real`
- `Cadena`
- `Caracter`
- `Logico`
- `Booleano`

```text
Algoritmo Variables
    Definir edad Como Entero
    Definir precio Como Real
    Definir nombre Como Cadena
    Definir activo Como Logico

    edad <- 18
    precio <- 12.5
    nombre <- "Ada"
    activo <- Verdadero

    Escribir nombre, " tiene ", edad, " anos"
FinAlgoritmo
```

## Asignaciones

Se asigna con `<-`.

```text
Algoritmo Asignaciones
    Definir total Como Entero

    total <- 10
    total <- total + 5

    Escribir "Total: ", total
FinAlgoritmo
```

## Operadores

Operadores aritmeticos:

- `+` suma
- `-` resta
- `*` multiplicacion
- `/` division
- `%` modulo

Operadores de comparacion:

- `=` igual
- `<>` diferente
- `<` menor que
- `<=` menor o igual
- `>` mayor que
- `>=` mayor o igual

Operadores logicos:

- `Y`
- `O`
- `NO`

```text
Algoritmo Operadores
    Definir edad Como Entero
    Definir tienePermiso Como Logico

    edad <- 20
    tienePermiso <- Verdadero

    Si edad >= 18 Y tienePermiso Entonces
        Escribir "Puede entrar"
    Sino
        Escribir "No puede entrar"
    FinSi
FinAlgoritmo
```

## Escribir

`Escribir` muestra texto, numeros, variables o expresiones.

```text
Algoritmo Salida
    Definir numero Como Entero

    numero <- 7

    Escribir "Numero: ", numero
    Escribir "Doble: ", numero * 2
FinAlgoritmo
```

## Leer

`Leer` pide un dato. La ejecucion se pausa hasta que escribas el valor en la consola inferior y presiones enviar.

```text
Algoritmo Entrada
    Definir nombre Como Cadena
    Definir edad Como Entero

    Escribir "Nombre:"
    Leer nombre

    Escribir "Edad:"
    Leer edad

    Escribir "Hola ", nombre, ". Edad: ", edad
FinAlgoritmo
```

## Si, Sino y FinSi

`Si` ejecuta un bloque cuando la condicion es verdadera. `Sino` es opcional.

```text
Algoritmo MayorEdad
    Definir edad Como Entero

    edad <- 17

    Si edad >= 18 Entonces
        Escribir "Mayor de edad"
    Sino
        Escribir "Menor de edad"
    FinSi
FinAlgoritmo
```

Tambien puedes anidar condiciones.

```text
Algoritmo Notas
    Definir nota Como Entero

    nota <- 92

    Si nota >= 90 Entonces
        Escribir "Excelente"
    Sino
        Si nota >= 70 Entonces
            Escribir "Aprobado"
        Sino
            Escribir "Necesita practicar"
        FinSi
    FinSi
FinAlgoritmo
```

## Mientras

`Mientras` repite un bloque mientras la condicion sea verdadera.

```text
Algoritmo ContadorMientras
    Definir contador Como Entero

    contador <- 1

    Mientras contador <= 5 Hacer
        Escribir "Contador: ", contador
        contador <- contador + 1
    FinMientras
FinAlgoritmo
```

## Para

`Para` repite usando un contador. En esta version el paso inicial soportado es ascendente de `1`.

```text
Algoritmo ContadorPara
    Definir i Como Entero

    Para i <- 1 Hasta 5 Hacer
        Escribir "i = ", i
    FinPara
FinAlgoritmo
```

## Segun

`Segun` elige un bloque segun el valor de una expresion. Usa casos con `valor:` y `De Otro Modo:` para el caso por defecto.

```text
Algoritmo Menu
    Definir opcion Como Entero

    opcion <- 2

    Segun opcion Hacer
        1:
            Escribir "Nuevo archivo"
        2:
            Escribir "Abrir archivo"
        3:
            Escribir "Guardar archivo"
        De Otro Modo:
            Escribir "Opcion desconocida"
    FinSegun
FinAlgoritmo
```

## Estructuras anidadas

Puedes mezclar estructuras.

```text
Algoritmo Tabla
    Definir i Como Entero
    Definir resultado Como Entero

    Para i <- 1 Hasta 5 Hacer
        resultado <- i * 2

        Si resultado >= 6 Entonces
            Escribir i, " x 2 = ", resultado, " grande"
        Sino
            Escribir i, " x 2 = ", resultado
        FinSi
    FinPara
FinAlgoritmo
```

## Leer dentro de estructuras

La entrada interactiva tambien funciona dentro de bloques.

```text
Algoritmo SumarHastaCero
    Definir numero Como Entero
    Definir suma Como Entero

    suma <- 0
    numero <- 1

    Mientras numero <> 0 Hacer
        Escribir "Numero, 0 para terminar:"
        Leer numero
        suma <- suma + numero
    FinMientras

    Escribir "Suma final: ", suma
FinAlgoritmo
```

## Ejemplo completo

```text
Algoritmo Promedio
    Definir cantidad Como Entero
    Definir i Como Entero
    Definir nota Como Real
    Definir suma Como Real
    Definir promedio Como Real

    Escribir "Cuantas notas?"
    Leer cantidad

    suma <- 0

    Para i <- 1 Hasta cantidad Hacer
        Escribir "Nota ", i, ":"
        Leer nota
        suma <- suma + nota
    FinPara

    promedio <- suma / cantidad

    Si promedio >= 70 Entonces
        Escribir "Aprobado con promedio ", promedio
    Sino
        Escribir "No aprobado. Promedio ", promedio
    FinSi
FinAlgoritmo
```

## Formato e indentacion

El editor puede alinear el codigo automaticamente:

- Enter conserva el indentado actual.
- Despues de `Algoritmo`, `Si`, `Mientras`, `Para` y `Segun`, Enter agrega un nivel.
- `Sino`, `De Otro Modo` y los casos de `Segun` continuan con un bloque indentado.
- Puedes usar **Editar > Formatear documento** o `Ctrl+K, Ctrl+D`.

Ejemplo sin formato:

```text
Algoritmo MalIndentado
Definir edad Como Entero
edad <- 20
Si edad >= 18 Entonces
Escribir "Adulto"
Sino
Escribir "Menor"
FinSi
FinAlgoritmo
```

Despues de formatear:

```text
Algoritmo MalIndentado
    Definir edad Como Entero
    edad <- 20
    Si edad >= 18 Entonces
        Escribir "Adulto"
    Sino
        Escribir "Menor"
    FinSi
FinAlgoritmo
```

## Diagnosticos

La app marca errores de sintaxis en vivo y muestra problemas con linea, causa y posible solucion.

```text
Algoritmo ErrorEjemplo
    Si edad >= 18 Entonces
        Escribir "Falta cerrar el bloque"
FinAlgoritmo
```

En este caso debe aparecer un diagnostico porque falta `FinSi`.

## Depuracion paso a paso

Puedes iniciar una sesion desde **Ejecutar > Iniciar depuracion** y avanzar con `F10`. La app selecciona la linea actual y actualiza salida, diagnosticos y variables.

```text
Algoritmo Depurar
    Definir contador Como Entero

    contador <- 1

    Mientras contador <= 3 Hacer
        Escribir "Paso ", contador
        contador <- contador + 1
    FinMientras
FinAlgoritmo
```

## Configuracion del lenguaje

La sintaxis puede cambiar mediante dialectos JSON. Por ejemplo, un dialecto puede reemplazar `Escribir` por `Mostrar`. No hay aliases: si cambias una palabra, la anterior deja de ser valida en ese dialecto.

Lee tambien [Configuracion JSON](configuracion-json.md).
