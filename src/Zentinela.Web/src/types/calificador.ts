export interface CarteraLiderDropdownDto {
  carteraID: number;
  liderID: number;
  etiqueta: string;
}

export interface DesglosePar {
  etiqueta: string;
  cantidad: number;
  porcentaje: number;
}

export interface CalificadorCarteraDto {
  // Bloque 1
  totalTitulares: number;

  // Bloque 2: gestión
  gestionDisponible: boolean;
  titularesGestionados: number;
  saldoTotal: number;
  saldoPromedio: number;
  titularesConAcuerdoVigente: number;
  valorTotalPromesas: number;

  // Bloque 3: rotación
  rotacionPromedio: number | null;
  desgloseRotacion: DesglosePar[];

  // Bloque 4: tipificación
  desglosePerfil: DesglosePar[];

  // Bloque 4: Fosyga
  titularesConFosyga: number;
  desgloseTipoAfiliado: DesglosePar[];

  // Bloque 5: régimen
  desgloseRegimen: DesglosePar[];

  // Bloque 6: EPS
  desgloseEPS: DesglosePar[];

  // Bloque 7: departamento
  desgloseDepartamento: DesglosePar[];

  // Bloque 8: contactabilidad
  conCelular: number;
  conEmail: number;
  conCelularYEmail: number;
  sinContacto: number;
  desgloseFuentesCelular: DesglosePar[];

  // Bloque 9: edad Registraduría
  conEdadRegistraduria: number;
  sinEdadRegistraduria: number;
  edadPromedioRegistraduria: number | null;

  // Bloque 10: localización por fuente
  localizacion: LocalizacionFuente[];
}

export interface LocalizacionFuente {
  fuente: string;
  encontrados: number;
  noEncontrados: number;
  faltanPorBuscar: number;
}
