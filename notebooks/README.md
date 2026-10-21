# 📓 TRISAT Interactive Google Colab Notebooks

Cuadernos interactivos ejecutables directamente en **Google Colab** o **JupyterLab** que documentan la física de señal, criptografía **Galileo OSNMA** y posicionamiento de alta precisión **Galileo E6 HAS (`HAS-UA`)** implementados en **TRISAT**.

| # | Cuaderno | Descripción | Google Colab |
| :-: | :--- | :--- | :-: |
| **01** | [`01_galileo_inav_demo.ipynb`](./01_galileo_inav_demo.ipynb) | Introducción a la estructura de página **Galileo E1-B I/NAV** y extracción básica de los 40 bits reservados para **OSNMA** (`8b HKROOT` + `32b MACK`). | [![Open In Colab](https://colab.research.google.com/assets/colab-badge.svg)](https://colab.research.google.com/github/jreyes13/TRISAT/blob/main/notebooks/01_galileo_inav_demo.ipynb) |
| **02** | [`02_euspa_osnma_has_complete_suite.ipynb`](./02_euspa_osnma_has_complete_suite.ipynb) | **Suite Completa EUSPA:** Modulación `CBOC(6,1,1/11)` vs `BPSK(5)`, validación criptográfica **OSNMA v1.1 (Vectores Reales Anexo A: Árbol de Merkle, ECDSA P-256, Cadena TESLA, `MACSEQ` y `HMAC-SHA-256`)**, algoritmo **Galileo HAS-UA (`RS(255,32)`, `NTW->ECEF`, `PPPSD`, `DIA`)**, ionosfera **NTCM-G** y geocerca **EWSS-CAMF (122 bits)**. | [![Open In Colab](https://colab.research.google.com/assets/colab-badge.svg)](https://colab.research.google.com/github/jreyes13/TRISAT/blob/main/notebooks/02_euspa_osnma_has_complete_suite.ipynb) |
