import type { DesignDocument, PrintArea } from '../design/model';

export interface Variant {
  id: string;
  sku: string;
  colorName: string;
  colorHex: string;
  size: string | null;
  price: number;
}

export interface Customization {
  kind: 'Apparel' | 'Drinkware' | 'Accessory' | string;
  modelUrl: string | null;
  baseFee: number;
  additionalPrintAreaFee: number;
  maxElementsPerArea: number;
  minRecommendedDpi: number;
  printAreas: PrintArea[];
  fonts: string[];
}

export interface ProductDetail {
  id: string;
  name: string;
  slug: string;
  shortDescription: string;
  categorySlug: string;
  price: number;
  currency: string;
  isCustomizable: boolean;
  variants: Variant[];
  customization: Customization | null;
}

export interface UploadDto {
  id: string;
  url: string;
  kind: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  width: number;
  height: number;
  qualityWarning: string | null;
}

export interface CartLine {
  id: string;
  productId: string;
  productSlug: string;
  variant: { id: string } | null;
  quantity: number;
  isCustomized: boolean;
  design: DesignDocument | null;
  previewUrl: string | null;
}

export interface CartResponse {
  items: CartLine[];
  itemCount: number;
}

export interface DesignDto {
  id: string;
  productId: string;
  productSlug: string;
  name: string;
  design: DesignDocument;
}

export interface DesignWarning {
  code: string;
  message: string;
  elementId: string | null;
  effectiveDpi: number | null;
}

export interface ValidationResult {
  isValid: boolean;
  errors: Record<string, string[]>;
  warnings: DesignWarning[];
}
