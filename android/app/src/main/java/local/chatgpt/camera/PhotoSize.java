package local.chatgpt.camera;

final class PhotoSize {
    static int choose(int[][] sizes) {
        if (sizes.length == 0) throw new IllegalArgumentException("No photo sizes");
        int best = -1; double error = Double.MAX_VALUE; long area = 0;
        for (int i = 0; i < sizes.length; i++) {
            int w = sizes[i][0], h = sizes[i][1]; long pixels = (long) w * h;
            if (w <= 0 || h <= 0 || pixels > 5_000_000) continue;
            double ratioError = Math.abs((double) Math.max(w, h) / Math.min(w, h) - 4.0 / 3.0);
            if (ratioError < error - 0.001 || (Math.abs(ratioError - error) < 0.001 && pixels > area)) { best = i; error = ratioError; area = pixels; }
        }
        if (best >= 0) return best;
        // If this camera offers no budget-sized image, choose its smallest valid output.
        area = Long.MAX_VALUE;
        for (int i = 0; i < sizes.length; i++) {
            long pixels = (long) sizes[i][0] * sizes[i][1];
            if (sizes[i][0] > 0 && sizes[i][1] > 0 && pixels < area) { best = i; area = pixels; }
        }
        if (best < 0) throw new IllegalArgumentException("Invalid photo sizes");
        return best;
    }
}
