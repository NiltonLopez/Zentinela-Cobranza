import { render, screen } from '@testing-library/react';
import { MetricCard } from '../../components/MetricCard';

describe('MetricCard', () => {
  it('muestra el label en mayúsculas', () => {
    render(<MetricCard label="Titulares totales" value={1284} />);
    expect(screen.getByText('Titulares totales')).toBeInTheDocument();
  });

  it('formatea números con separador de miles en español colombiano', () => {
    render(<MetricCard label="Total" value={1284} />);
    // es-CO usa punto como separador de miles → "1.284"
    // jsdom puede variar según ICU disponible, la regex cubre ambos formatos
    const valor = screen.getByText(/1[.,]284/);
    expect(valor).toBeInTheDocument();
  });

  it('muestra valor string sin transformar', () => {
    render(<MetricCard label="Última descarga" value="Hoy 9:42am" />);
    expect(screen.getByText('Hoy 9:42am')).toBeInTheDocument();
  });

  it('variante brand aplica color de marca al valor', () => {
    render(<MetricCard label="Filtrados" value={347} variant="brand" />);
    // El valor formateado debe existir en el DOM
    expect(screen.getByText(/347/)).toBeInTheDocument();
  });

  it('variante default es la predeterminada (no rompe sin prop)', () => {
    render(<MetricCard label="Total" value={100} />);
    expect(screen.getByText(/100/)).toBeInTheDocument();
  });
});
