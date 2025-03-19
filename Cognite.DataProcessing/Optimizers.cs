using System;

namespace Cognite.DataProcessing
{
    /// <summary>
    /// Class containing routines for optimization
    /// </summary>
    public static class Optimizers
    {
        /// <summary>
        /// Represents the optimization result.
        /// </summary>
        public class OptimizeResult
        {
            /// <summary>
            /// Parameters (over given interval) which minimize the objective function.
            /// </summary>
            public double X { get; }
            /// <summary>
            /// Whether or not the optimizer exited successfully.
            /// </summary>
            public bool Success { get; }
            /// <summary>
            /// Termination status of the optimizer. Refer to Message for details.
            /// </summary>
            public int Status { get; }
            /// <summary>
            /// Description of the cause of the termination.
            /// </summary>
            public string Message { get; }
            /// <summary>
            /// Value of the objective function
            /// </summary>
            public double Fun { get; }
            /// <summary>
            /// Number of evaluations of the objective function
            /// </summary>
            public int NfEv { get; }

            /// <summary>
            /// Represents the optimization result.
            /// </summary>
            /// <param name="x">The solution of the optimization.</param>
            /// <param name="success">Whether or not the optimizer exited successfully.</param>
            /// <param name="status">Termination status of the optimizer. Its value depends on the underlying solver.
            /// Refer to `message` for details.</param>
            /// <param name="message">Description of the cause of the termination.</param>
            /// <param name="fun">Value of the objective function.</param>
            /// <param name="nfEv">Number of evaluations of the objective function.</param>
            public OptimizeResult(double x, bool success, int status, string message, double fun, int nfEv)
            {
                this.X = x;
                this.Success = success;
                this.Status = status;
                this.Message = message;
                this.Fun = fun;
                this.NfEv = nfEv;
            }
        }

        /// <summary>
        /// Bounded minimization for scalar functions.
        ///
        /// Based on the `fminbound` function from scipy:
        /// https://docs.scipy.org/doc/scipy/reference/generated/scipy.optimize.fminbound.html
        /// </summary>
        /// <param name="f">Objective function to be minimized (must accept and return scalars).</param>
        /// <param name="lowerBound">The low value of the range where the root is supposed to be.</param>
        /// <param name="upperBound">The high value of the range where the root is supposed to be.</param>
        /// <param name="accuracy">The convergence tolerance.</param>
        /// <param name="maxIterations">Maximum number of iterations allowed.</param>
        /// <returns>`OptimizeResult` object</returns>
        /// <exception cref="ArgumentException">When lowerBound is greater than the upperBound</exception>
        public static OptimizeResult MinimizeScalarBounded(
            Func<double, double> f, double lowerBound, double upperBound, double accuracy = 1e-6, int maxIterations = 100)
        {
            if (f == null)
                throw new ArgumentNullException(nameof(f), "The function cannot be null");

            if (lowerBound > upperBound)
                throw new ArgumentException("lowerBound must be less than or equal to upperBound.");

            int flag = 0;

            double sqrtEps = Math.Sqrt(2.2e-16);
            double goldenMean = 0.5 * (3.0 - Math.Sqrt(5.0));
            var (a, b) = (lowerBound, upperBound);
            double fulc = a + goldenMean * (b - a);
            var (nfc, xf) = (fulc, fulc);
            var (rat, e) = (0.0, 0.0);
            double x = xf;
            double fx = f(x);
            int num = 1;
            double fu = double.PositiveInfinity;

            (double ffulc, double fnfc) = (fx, fx);
            double xm = 0.5 * (a + b);
            double tol1 = sqrtEps * Math.Abs(xf) + accuracy / 3.0;
            double tol2 = 2.0 * tol1;

            while (Math.Abs(xf - xm) > (tol2 - 0.5 * (b - a)))
            {
                int golden = 1;
                // Check for parabolic fit
                int si;
                int ez;
                if (Math.Abs(e) > tol1)
                {
                    golden = 0;
                    double r = (xf - nfc) * (fx - ffulc);
                    double q = (xf - fulc) * (fx - fnfc);
                    double p = (xf - fulc) * q - (xf - nfc) * r;
                    q = 2.0 * (q - r);
                    if (q > 0.0)
                        p = -p;
                    q = Math.Abs(q);
                    r = e;
                    e = rat;

                    // Check for acceptability of parabola
                    if ((Math.Abs(p) < Math.Abs(0.5 * q * r)) && (p > q * (a - xf)) && (p < q * (b - xf)))
                    {
                        rat = (p + 0.0) / q;
                        x = xf + rat;
                        if (((x - a) < tol2) || ((b - x) < tol2))
                        {
                            ez = ((xm - xf) == 0) ? 1 : 0;
                            si = Math.Sign(xm - xf) + ez;
                            rat = tol1 * si;
                        }
                    }
                    else // do a golden-section step
                        golden = 1;
                }
                // do a golden-section step
                if (golden == 1)
                {
                    if (xf >= xm)
                        e = a - xf;
                    else
                        e = b - xf;
                    rat = goldenMean * e;
                }

                ez = (rat == 0) ? 1 : 0;
                si = Math.Sign(rat) + ez;
                x = xf + si * Math.Max(Math.Abs(rat), tol1);
                fu = f(x);
                num += 1;

                if (fu <= fx)
                {
                    if (x >= xf)
                        a = xf;
                    else
                        b = xf;
                    (fulc, ffulc) = (nfc, fnfc);
                    (nfc, fnfc) = (xf, fx);
                    (xf, fx) = (x, fu);
                }
                else
                {
                    if (x < xf)
                        a = x;
                    else
                        b = x;
                    if ((fu <= fnfc) || (nfc == xf))
                    {
                        (fulc, ffulc) = (nfc, fnfc);
                        (nfc, fnfc) = (x, fu);
                    }
                    else if ((fu <= ffulc) || (fulc == xf) || (fulc == nfc))
                    {
                        (fulc, ffulc) = (x, fu);
                    }
                }
                xm = 0.5 * (a + b);
                tol1 = sqrtEps * Math.Abs(xf) + accuracy / 3.0;
                tol2 = 2.0 * tol1;

                if (num >= maxIterations)
                {
                    flag = 1;
                    break;
                }
            }

            if (double.IsNaN(xf) || double.IsNaN(fx) || double.IsNaN(fu))
                flag = 2;

            string message;
            switch (flag)
            {
                case 0:
                    message = "Solution found";
                    break;
                case 1:
                    message = "Maximum number of function calls reached";
                    break;
                case 2:
                    message = "NaN result encountered";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return new OptimizeResult(x: xf, success: flag == 0, message: message, status: flag, fun: fx, nfEv: num);
        }
    }
}
